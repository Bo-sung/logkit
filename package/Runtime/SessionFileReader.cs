using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

namespace LogKit
{

    /// <summary>
    /// 세션 JSONL 파일을 바이트 위치 기준으로 읽는 정적 읽기기다.
    /// 기록 워커가 같은 파일에 계속 덧붙이는 중에도 읽을 수 있도록 공유 모드로 열고, 줄바꿈으로 끝난 줄만 읽는다.
    /// 전송 스레드에서 쓰므로 Unity API 중 스레드에 안전한 JsonUtility만 사용한다.
    /// </summary>
    public static class SessionFileReader
    {
        private const int READ_BUFFER_BYTES = 64 * 1024;
        private const byte NEW_LINE = (byte)'\n';

        /// <summary>
        /// 첫 줄의 세션 헤더를 읽는다.
        /// 파일 경로를 사용하며, 헤더와 헤더 줄의 바이트 길이(첫 항목이 시작하는 위치)를 반환한다.
        /// 헤더 줄이 아직 줄바꿈으로 끝나지 않았거나 파싱할 수 없으면 false를 반환한다.
        /// </summary>
        public static bool TryReadHeader(string filePath, out LogSessionHeader header, out long headerBytes)
        {
            header = null;
            headerBytes = 0;
            try
            {
                using (FileStream stream = OpenShared(filePath))
                using (MemoryStream line = new MemoryStream())
                {
                    int value;
                    while ((value = stream.ReadByte()) >= 0)
                    {
                        if (value == NEW_LINE)
                        {
                            headerBytes = line.Length + 1;
                            header = JsonUtility.FromJson<LogSessionHeader>(DecodeLine(line.GetBuffer(), 0, (int)line.Length));
                            return header != null && !string.IsNullOrEmpty(header.sessionId);
                        }
                        line.WriteByte((byte)value);
                    }
                }
                return false;
            }
            catch
            {
                header = null;
                headerBytes = 0;
                return false;
            }
        }

        /// <summary>
        /// offset 위치부터 다음 파트에 들어갈 항목을 읽는다.
        /// 최대 건수와 최대 바이트(항목 줄의 UTF-8 크기 합) 중 먼저 닿는 쪽에서 끊되, 항목 하나가 최대 바이트보다 커도 최소 1건은 담는다.
        /// 줄바꿈으로 끝나지 않은 마지막 줄은 기록 중일 수 있어 남겨 두며, includeUnterminated가 true(기록이 끝난 파일)면 그 줄까지 소비한다.
        /// 파싱할 수 없는 줄(크래시로 잘린 줄 등)은 건너뛰고 소비한 것으로 친다.
        /// 항목 배열과 소비한 바이트 수, 한도에 걸려 뒤에 더 읽을 줄이 남았는지를 반환하며, 입출력 실패 시 false를 반환한다.
        /// </summary>
        public static bool TryReadFrom(string filePath, long offset, int maxEntries, long maxBytes, bool includeUnterminated,
            out LogEntry[] entries, out long consumedBytes, out bool hasMore)
        {
            entries = new LogEntry[0];
            consumedBytes = 0;
            hasMore = false;
            try
            {
                List<LogEntry> buffer = new List<LogEntry>();
                long entryBytes = 0;
                long consumed = 0;
                using (FileStream stream = OpenShared(filePath))
                using (MemoryStream line = new MemoryStream())
                {
                    if (offset > stream.Length)
                        return false;
                    stream.Seek(offset, SeekOrigin.Begin);

                    byte[] chunk = new byte[READ_BUFFER_BYTES];
                    bool stop = false;
                    int read;
                    while (!stop && (read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            if (chunk[i] != NEW_LINE)
                            {
                                line.WriteByte(chunk[i]);
                                continue;
                            }

                            long lineBytes = line.Length + 1;
                            if (!TryTakeLine(line, lineBytes, buffer, ref entryBytes, maxBytes))
                            {
                                hasMore = true;
                                stop = true;
                                break;
                            }
                            consumed += lineBytes;
                            line.SetLength(0);

                            if (buffer.Count >= maxEntries)
                            {
                                hasMore = true;
                                stop = true;
                                break;
                            }
                        }
                    }

                    // 줄바꿈 없이 끝난 꼬리는 기록이 끝난 파일에서만 마지막 줄로 처리한다.
                    if (!stop && includeUnterminated && line.Length > 0)
                    {
                        if (TryTakeLine(line, line.Length, buffer, ref entryBytes, maxBytes))
                            consumed += line.Length;
                        else
                            hasMore = true;
                    }
                }

                entries = buffer.ToArray();
                consumedBytes = consumed;
                return true;
            }
            catch
            {
                entries = new LogEntry[0];
                consumedBytes = 0;
                hasMore = false;
                return false;
            }
        }

        /// <summary>
        /// 버퍼에 모인 한 줄을 항목 목록에 넣는다.
        /// 넣으면 최대 바이트를 넘는 경우(이미 1건 이상 있을 때)에만 false를 반환해 다음 파트로 미룬다.
        /// 빈 줄과 파싱할 수 없는 줄은 넣지 않고 true를 반환해 소비한 것으로 친다.
        /// </summary>
        private static bool TryTakeLine(MemoryStream line, long lineBytes, List<LogEntry> buffer, ref long entryBytes, long maxBytes)
        {
            string text = DecodeLine(line.GetBuffer(), 0, (int)line.Length);
            if (string.IsNullOrWhiteSpace(text))
                return true;
            if (buffer.Count > 0 && entryBytes + lineBytes > maxBytes)
                return false;

            LogEntry entry = ParseEntry(text);
            if (entry == null)
                return true;
            buffer.Add(entry);
            entryBytes += lineBytes;
            return true;
        }

        /// <summary>기록 워커의 덧붙이기·삭제와 충돌하지 않는 공유 읽기 모드로 연다.</summary>
        private static FileStream OpenShared(string filePath)
        {
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, READ_BUFFER_BYTES);
        }

        /// <summary>UTF-8 바이트를 문자열로 바꾸고 끝의 CR을 뗀다.</summary>
        private static string DecodeLine(byte[] bytes, int index, int count)
        {
            if (count > 0 && bytes[index + count - 1] == (byte)'\r')
                count--;
            return Encoding.UTF8.GetString(bytes, index, count);
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
