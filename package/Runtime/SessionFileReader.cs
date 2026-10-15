using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

namespace LogKit
{

    /// <summary>
    /// 세션 JSONL 파일을 처음부터 한 번만 순차로 읽으며 업로드용 파트를 잘라 내는 읽기기다.
    /// 파트마다 파일을 다시 열어 앞부분을 건너뛰지 않으므로 세션이 길어도 읽기 비용이 파일 크기에 비례한다.
    /// 전송 스레드에서 쓰므로 Unity API 중 스레드에 안전한 JsonUtility만 사용한다.
    /// </summary>
    public sealed class SessionFileReader : IDisposable
    {
        private readonly IEnumerator<string> _lines;
        private string _pendingLine;
        private bool _ended;

        /// <summary>파일 첫 줄의 세션 헤더.</summary>
        public LogSessionHeader Header { get; }

        private SessionFileReader(IEnumerator<string> lines, LogSessionHeader header)
        {
            _lines = lines;
            Header = header;
        }

        /// <summary>
        /// 세션 파일을 열고 첫 줄 헤더를 읽는다.
        /// 파일 경로를 입력으로 사용하며, 파일이 없거나 헤더가 유효하지 않으면 null을 반환한다.
        /// </summary>
        public static SessionFileReader TryOpen(string filePath)
        {
            IEnumerator<string> lines = null;
            try
            {
                lines = File.ReadLines(filePath).GetEnumerator();
                if (!lines.MoveNext())
                {
                    lines.Dispose();
                    return null;
                }

                LogSessionHeader header = JsonUtility.FromJson<LogSessionHeader>(lines.Current);
                if (header == null || string.IsNullOrEmpty(header.sessionId))
                {
                    lines.Dispose();
                    return null;
                }

                return new SessionFileReader(lines, header);
            }
            catch
            {
                lines?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// 다음 파트에 들어갈 항목을 읽는다.
        /// 최대 건수와 최대 바이트(항목 JSON의 UTF-8 크기 합)를 사용하며, 둘 중 먼저 닿는 쪽에서 끊는다.
        /// 항목 하나가 최대 바이트보다 커도 최소 1건은 담는다. 파싱할 수 없는 줄(크래시로 잘린 마지막 줄 등)은 건너뛴다.
        /// 읽기 성공 여부와 항목 배열, 이 파트 뒤에 남은 항목이 없는지를 반환하며, 입출력 실패 시 false를 반환한다.
        /// </summary>
        public bool TryReadNext(int maxEntries, long maxBytes, out LogEntry[] entries, out bool reachedEnd)
        {
            entries = new LogEntry[0];
            reachedEnd = true;
            try
            {
                List<LogEntry> buffer = new List<LogEntry>();
                long bytes = 0;
                while (buffer.Count < maxEntries && TryTakeLine(out string line))
                {
                    long size = Encoding.UTF8.GetByteCount(line);
                    if (buffer.Count > 0 && bytes + size > maxBytes)
                    {
                        // 이번 파트에 넣지 못한 줄은 다음 파트의 첫 항목으로 미룬다.
                        _pendingLine = line;
                        break;
                    }

                    LogEntry entry = ParseEntry(line);
                    if (entry == null)
                        continue;
                    buffer.Add(entry);
                    bytes += size;
                }

                entries = buffer.ToArray();
                reachedEnd = !HasMoreLines();
                return true;
            }
            catch
            {
                entries = new LogEntry[0];
                reachedEnd = true;
                return false;
            }
        }

        public void Dispose()
        {
            _lines.Dispose();
        }

        /// <summary>미뤄 둔 줄이 있으면 그것을, 없으면 파일의 다음 비어 있지 않은 줄을 꺼낸다.</summary>
        private bool TryTakeLine(out string line)
        {
            if (_pendingLine != null)
            {
                line = _pendingLine;
                _pendingLine = null;
                return true;
            }

            while (!_ended && _lines.MoveNext())
            {
                line = _lines.Current;
                if (!string.IsNullOrWhiteSpace(line))
                    return true;
            }

            _ended = true;
            line = null;
            return false;
        }

        /// <summary>남은 줄이 있는지 확인한다. 확인하려고 읽은 줄은 다음 파트로 미룬다.</summary>
        private bool HasMoreLines()
        {
            if (_pendingLine != null)
                return true;
            if (!TryTakeLine(out string line))
                return false;
            _pendingLine = line;
            return true;
        }

        /// <summary>한 줄을 항목으로 파싱한다. 파싱에 실패하면 null을 반환한다.</summary>
        private static LogEntry ParseEntry(string line)
        {
            try
            {
                LogEntry entry = JsonUtility.FromJson<LogEntry>(line);
                if (entry == null)
                    return null;
                entry.timestampUtc ??= "";
                entry.level ??= "";
                entry.category ??= "";
                entry.scene ??= "";
                entry.message ??= "";
                entry.dataJson ??= "";
                entry.stackTrace ??= "";
                return entry;
            }
            catch
            {
                return null;
            }
        }
    }
}
