using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

namespace LogKit
{

    /// <summary>
    /// 세션 로그를 JSONL 파일로 기록하고 업로드용 배치로 다시 읽는다.
    /// 로그 디렉터리와 세션 ID를 사용하며, 파일 생성·추가·pending 및 corrupt 전환을 담당한다.
    /// </summary>
    public class LogFileWriter
    {
        private const string FILE_EXTENSION = ".jsonl";
        private const string PENDING_SUFFIX = ".pending";
        private const string CORRUPT_SUFFIX = ".corrupt";

        private readonly string _filePath;
        private readonly object _writeLock = new object();

        public string CurrentFilePath => _filePath;

        /// <summary>
        /// 작성기를 준비한다.
        /// 로그 디렉터리와 세션 ID를 사용하며, 세션 파일의 전체 경로를 결정한다.
        /// </summary>
        public LogFileWriter(string logDirectory, string sessionId)
        {
            _filePath = Path.Combine(logDirectory, sessionId + FILE_EXTENSION);
        }

        /// <summary>
        /// 세션 파일을 생성하고 첫 줄에 헤더를 기록한다.
        /// header를 사용하며, 같은 경로의 기존 파일이 있으면 덮어쓴다.
        /// </summary>
        public void WriteSession(LogSessionHeader header)
        {
            string line = JsonUtility.ToJson(header);
            lock (_writeLock)
            {
                File.WriteAllText(_filePath, line + Environment.NewLine);
            }
        }

        /// <summary>
        /// 로그 항목들을 파일 끝에 한 줄씩 추가한다.
        /// entries를 사용하며, 배치마다 열고 닫아 크래시 시에도 기록분이 보존되게 한다.
        /// </summary>
        public void AppendEntries(List<LogEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            StringBuilder builder = new StringBuilder();
            foreach (LogEntry entry in entries)
                builder.AppendLine(JsonUtility.ToJson(entry));

            lock (_writeLock)
            {
                File.AppendAllText(_filePath, builder.ToString());
            }
        }

        /// <summary>
        /// 업로드 대상이 될 수 있는 파일 경로 목록을 반환한다.
        /// 로그 디렉터리와 현재 세션 ID를 사용하며, 현재 세션 파일을 제외한 남은 세션 파일과 pending 파일을 포함한다.
        /// </summary>
        public static List<string> GetUploadableFilePaths(string logDirectory, string currentSessionId)
        {
            List<string> paths = new List<string>();
            if (!Directory.Exists(logDirectory))
                return paths;

            string currentFileName = currentSessionId + FILE_EXTENSION;
            foreach (string path in Directory.GetFiles(logDirectory, "*" + FILE_EXTENSION))
            {
                if (Path.GetFileName(path) == currentFileName)
                    continue;
                paths.Add(path);
            }

            foreach (string path in Directory.GetFiles(logDirectory, "*" + FILE_EXTENSION + PENDING_SUFFIX))
                paths.Add(path);

            return paths;
        }

        /// <summary>
        /// 세션 파일을 읽어 업로드 배치로 조립한다.
        /// 파일 경로와 정상 종료 여부, 항목 상한을 사용하며, 읽기에 실패하면 false를 반환한다.
        /// </summary>
        public static bool TryReadBatch(string filePath, bool endedCleanly, int maxEntries, out UploadBatch batch)
        {
            batch = null;
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length == 0)
                    return false;

                LogSessionHeader header = JsonUtility.FromJson<LogSessionHeader>(lines[0]);
                if (header == null || string.IsNullOrEmpty(header.sessionId))
                    return false;

                List<LogEntry> entries = new List<LogEntry>();
                bool truncated = false;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;
                    if (entries.Count >= maxEntries)
                    {
                        truncated = true;
                        break;
                    }

                    LogEntry entry = JsonUtility.FromJson<LogEntry>(lines[i]);
                    if (entry == null)
                        continue;
                    Normalize(entry);
                    entries.Add(entry);
                }

                batch = new UploadBatch
                {
                    sessionId = header.sessionId,
                    startedAtUtc = header.startedAtUtc,
                    endedAtUtc = entries.Count > 0 ? entries[entries.Count - 1].timestampUtc : DateTime.UtcNow.ToString("o"),
                    appVersion = header.appVersion,
                    unityVersion = header.unityVersion,
                    isEditor = header.isEditor,
                    platform = header.platform,
                    endedCleanly = endedCleanly,
                    truncated = truncated,
                    entries = entries.ToArray()
                };
                return true;
            }
            catch
            {
                batch = null;
                return false;
            }
        }

        /// <summary>
        /// 업로드에 실패한 파일을 pending 이름으로 바꿔 보존한다.
        /// 파일 경로를 사용하며, 다음 기동 재전송 대상이 되도록 한다.
        /// </summary>
        public static void MarkPending(string filePath)
        {
            RenameFile(filePath, PENDING_SUFFIX);
        }

        /// <summary>
        /// 읽기에 실패해 재시도해도 의미 없는 파일을 corrupt 이름으로 격리한다.
        /// 파일 경로를 사용하며, 재전송 목록에서 제외되게 한다.
        /// </summary>
        public static void MarkCorrupt(string filePath)
        {
            RenameFile(filePath, CORRUPT_SUFFIX);
        }

        /// <summary>
        /// 보존 기간이 지난 pending 파일을 삭제한다.
        /// 로그 디렉터리와 보존 일수를 사용하며, 기준이 지난 파일만 제거한다.
        /// </summary>
        public static void PruneExpiredFiles(string logDirectory, int retentionDays)
        {
            if (!Directory.Exists(logDirectory) || retentionDays <= 0)
                return;

            DateTime threshold = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (string path in Directory.GetFiles(logDirectory, "*" + FILE_EXTENSION + PENDING_SUFFIX))
            {
                if (File.GetLastWriteTimeUtc(path) >= threshold)
                    continue;
                TryDeleteFile(path);
            }
        }

        /// <summary>
        /// 파일을 삭제한다. 파일 경로를 사용하며, 실패해도 예외를 밖으로 내보내지 않는다.
        /// </summary>
        public static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
                // 삭제 실패 시 다음 기동에 다시 시도된다.
            }
        }

        /// <summary>
        /// 항목의 null 문자열 필드를 빈 문자열로 바꾼다.
        /// 저장 시 누락된 필드가 있어도 배치 직렬화가 안전하도록 한다.
        /// </summary>
        private static void Normalize(LogEntry entry)
        {
            entry.timestampUtc ??= "";
            entry.level ??= "";
            entry.category ??= "";
            entry.scene ??= "";
            entry.message ??= "";
            entry.dataJson ??= "";
            entry.stackTrace ??= "";
        }

        /// <summary>
        /// 파일에 상태 접미사를 붙여 이름을 바꾼다.
        /// 파일 경로와 접미사를 사용하며, 같은 이름이 이미 있으면 먼저 지운다.
        /// </summary>
        private static void RenameFile(string filePath, string suffix)
        {
            try
            {
                string renamedPath = filePath + suffix;
                if (File.Exists(renamedPath))
                    File.Delete(renamedPath);
                if (File.Exists(filePath))
                    File.Move(filePath, renamedPath);
            }
            catch
            {
                // 이름 바꾸기 실패 시 원본이 남아 다음 기동에 다시 시도된다.
            }
        }
    }
}
