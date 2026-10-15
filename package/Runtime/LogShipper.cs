using System;
using System.IO;

using UnityEngine;

namespace LogKit
{

    /// <summary>세션 파일 한 번 전송 시도의 결과.</summary>
    public enum ShipResult
    {
        /// <summary>보낼 것을 모두 보냈다(새 항목이 없던 경우 포함). 마무리 전송이면 파일을 지워도 된다.</summary>
        Sent,
        /// <summary>파일이 아직 없거나 헤더가 다 써지지 않아 이번에는 보낼 수 없다.</summary>
        NotReady,
        /// <summary>통신 실패 등으로 일부를 보내지 못했다. 보낸 데까지는 진행 위치에 남아 다음 시도가 이어서 보낸다.</summary>
        Failed,
        /// <summary>파일을 읽을 수 없거나 서버가 내용을 거부했다. 재시도해도 같은 결과다.</summary>
        Rejected
    }

    /// <summary>
    /// 세션 파일 하나를 이어 보내기 방식으로 서버에 전송한다.
    /// 마지막으로 보낸 바이트 위치와 다음 파트 번호를 진행 파일({세션ID}.progress.json)에 남겨,
    /// 매 시도마다 그 뒤에 새로 쌓인 줄만 파트로 만들어 보낸다. 크래시 후에도 다음 기동이 남은 부분만 보낸다.
    /// 주기 전송 스레드와 종료 전송 스레드가 같은 인스턴스를 부를 수 있으므로 시도 전체를 잠금으로 직렬화한다.
    /// </summary>
    public sealed class LogShipper
    {
        /// <summary>한 파트의 최대 항목 수.</summary>
        public const int MAX_PART_ENTRIES = 20000;

        /// <summary>
        /// 한 파트에 담는 항목 JSON의 최대 크기(UTF-8 바이트, 압축 전).
        /// 압축이 전혀 안 돼도 서버 기본 본문 상한(5MB gzip)을 넘지 않는다. 밀린 양이 많으면 여러 파트로 나눠 연달아 보낸다.
        /// </summary>
        public const long MAX_PART_BYTES = 4L * 1024 * 1024;

        private readonly object _shipLock = new object();
        private readonly string _filePath;
        private readonly string _progressPath;

        private LogSessionHeader _header;
        private long _offset;
        private int _nextPart;
        private bool _loaded;

        public string FilePath => _filePath;

        public LogShipper(string filePath)
        {
            _filePath = filePath;
            _progressPath = LogFileWriter.GetProgressPathFor(filePath);
        }

        /// <summary>
        /// 진행 위치 뒤에 쌓인 항목을 파트로 나눠 모두 보낸다.
        /// final이 true면 기록이 끝난 파일로 보고 줄바꿈 없는 꼬리까지 소비하며, 정상 종료인데 보낼 항목이 없으면 빈 파트 하나를 보내
        /// 종료 여부(endedCleanly)와 최신 지표를 서버에 남긴다. final이 아니면 새 항목이 있을 때만 보낸다.
        /// </summary>
        public ShipResult Ship(LogUploader uploader, bool final, bool endedCleanly)
        {
            lock (_shipLock)
            {
                if (uploader == null)
                    return ShipResult.Failed;
                if (!File.Exists(_filePath))
                    return ShipResult.NotReady;
                if (!_loaded && !TryLoad())
                    return final ? ShipResult.Rejected : ShipResult.NotReady;

                MetricSample[] metrics = ReadMetrics();
                bool sentAny = false;
                while (true)
                {
                    if (!SessionFileReader.TryReadFrom(_filePath, _offset, MAX_PART_ENTRIES, MAX_PART_BYTES, final,
                            out LogEntry[] entries, out long consumed, out bool hasMore))
                        return ShipResult.Failed;

                    // 마무리 때 보낼 항목이 없으면 정상 종료 표시나 지표를 남기려고 빈 파트를 보낸다.
                    // 크래시 세션을 다음 기동에 정리할 때는 이미 보낸 파트가 있으면 굳이 보내지 않는다.
                    bool sendEmptyFinal = final && !sentAny && !hasMore && entries.Length == 0 && (endedCleanly || _nextPart == 0);
                    if (entries.Length > 0 || sendEmptyFinal)
                    {
                        UploadResult result = uploader.Upload(CreateBatch(entries, metrics, final && endedCleanly));
                        if (result == UploadResult.Rejected)
                            return ShipResult.Rejected;
                        if (result != UploadResult.Success)
                            return ShipResult.Failed;
                        _nextPart++;
                        sentAny = true;
                    }

                    // 파싱할 수 없는 줄만 있던 구간도 진행 위치는 넘긴다.
                    if (consumed > 0 || entries.Length > 0 || sendEmptyFinal)
                    {
                        _offset += consumed;
                        SaveProgress();
                    }

                    if (!hasMore)
                        return ShipResult.Sent;
                }
            }
        }

        /// <summary>전송을 마친 세션의 로그·지표·진행 파일을 모두 지운다.</summary>
        public void DeleteFiles()
        {
            lock (_shipLock)
            {
                string metricsPath = LogFileWriter.GetMetricsPathFor(_filePath);
                LogFileWriter.TryDeleteFile(_filePath);
                LogFileWriter.TryDeleteFile(metricsPath);
                LogFileWriter.TryDeleteFile(metricsPath + LogFileWriter.PENDING_SUFFIX);
                LogFileWriter.TryDeleteFile(_progressPath);
            }
        }

        /// <summary>읽을 수 없거나 서버가 거부한 세션 파일을 corrupt로 격리하고 진행 파일을 지운다.</summary>
        public void Quarantine()
        {
            lock (_shipLock)
            {
                LogFileWriter.MarkCorrupt(_filePath);
                LogFileWriter.TryDeleteFile(_progressPath);
            }
        }

        /// <summary>
        /// 헤더와 진행 파일을 읽는다. 진행 파일이 없으면 첫 항목부터 파트 0으로 시작한다.
        /// 헤더 줄이 아직 다 써지지 않았거나 깨졌으면 false를 반환한다.
        /// </summary>
        private bool TryLoad()
        {
            if (!SessionFileReader.TryReadHeader(_filePath, out LogSessionHeader header, out long headerBytes))
                return false;

            _header = header;
            _offset = headerBytes;
            _nextPart = 0;
            try
            {
                if (File.Exists(_progressPath))
                {
                    ShipProgress progress = JsonUtility.FromJson<ShipProgress>(File.ReadAllText(_progressPath));
                    if (progress != null && progress.offset >= headerBytes && progress.nextPart >= 0)
                    {
                        _offset = progress.offset;
                        _nextPart = progress.nextPart;
                    }
                }
            }
            catch
            {
                // 진행 파일이 깨졌으면 처음부터 보낸다. 서버는 같은 파트 번호를 교체로 받는다.
            }

            _loaded = true;
            return true;
        }

        /// <summary>
        /// 진행 위치를 기록한다. 임시 파일에 쓴 뒤 교체해, 쓰는 도중 꺼져도 이전 값이 남게 한다.
        /// 서버가 받은 뒤 기록 전에 꺼지면 같은 파트를 다시 보내게 되며, 서버는 교체로 멱등 처리한다.
        /// </summary>
        private void SaveProgress()
        {
            try
            {
                string tempPath = _progressPath + ".tmp";
                File.WriteAllText(tempPath, JsonUtility.ToJson(new ShipProgress { offset = _offset, nextPart = _nextPart }));
                if (File.Exists(_progressPath))
                    File.Replace(tempPath, _progressPath, null);
                else
                    File.Move(tempPath, _progressPath);
            }
            catch
            {
                // 진행 기록 실패는 다음 기동에 일부를 다시 보내는 것으로 끝난다.
            }
        }

        /// <summary>지표 파일을 읽는다. 구버전이 pending으로 바꿔 둔 지표 파일도 찾는다.</summary>
        private MetricSample[] ReadMetrics()
        {
            string metricsPath = LogFileWriter.GetMetricsPathFor(_filePath);
            if (LogFileWriter.TryReadMetrics(metricsPath, out MetricSample[] metrics))
                return metrics;
            if (LogFileWriter.TryReadMetrics(metricsPath + LogFileWriter.PENDING_SUFFIX, out metrics))
                return metrics;
            return new MetricSample[0];
        }

        private UploadBatch CreateBatch(LogEntry[] entries, MetricSample[] metrics, bool endedCleanly)
        {
            return new UploadBatch
            {
                sessionId = _header.sessionId,
                startedAtUtc = _header.startedAtUtc,
                // 빈 파트는 정상 종료면 지금을, 크래시 정리면 시작 시각을 넣어 세션 길이가 정리 시점까지 늘어나지 않게 한다.
                endedAtUtc = entries.Length > 0 ? entries[entries.Length - 1].timestampUtc
                    : endedCleanly ? DateTime.UtcNow.ToString("o") : _header.startedAtUtc,
                appVersion = _header.appVersion,
                unityVersion = _header.unityVersion,
                isEditor = _header.isEditor,
                platform = _header.platform,
                endedCleanly = endedCleanly,
                truncated = false,
                partIndex = _nextPart,
                metrics = metrics,
                entries = entries
            };
        }
    }
}
