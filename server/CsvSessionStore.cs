using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace LogServer;

/// <summary>
/// 경로 단위로 세마포어를 나눠 주는 파일 잠금 헬퍼. 같은 파일을 쓰는 스레드만 직렬화한다.
/// path를 입력으로 사용하며 경로별 SemaphoreSlim을 정적 사전에 보관한다.
/// </summary>
internal static class FileLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    /// <summary>
    /// 지정한 파일 경로 전용의 잠금 객체를 돌려준다.
    /// path를 키로 사용하며, 없으면 새로 만들어 _gates에 추가한다.
    /// </summary>
    public static SemaphoreSlim Get(string path)
    {
        return _gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
    }
}

/// <summary>
/// 세션 원본 CSV와 raw gzip을 디스크에 기록하는 저장소.
/// sessionId와 로그 항목을 받아 data/sessions, data/raw 아래 파일을 교체 쓰며, 파일 단위 잠금으로 깨진 CSV를 막는다.
/// </summary>
public sealed class CsvSessionStore
{
    private const string SESSION_HEADER = "timestamp_utc,level,category,scene,message,data_json,stack_trace";
    private const string CRLF = "\r\n";
    private const int MAX_MESSAGE_CHARS = 2000;
    private const int MAX_STACK_CHARS = 8000;
    private const int MAX_DATA_CHARS = 4000;
    private const int MAX_SHORT_CHARS = 256;
    private const string TIMESTAMP_FORMAT = "yyyy-MM-dd HH:mm:ss.fff";

    private static readonly Regex _safeSessionId = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);
    private static readonly UTF8Encoding _utf8Bom = new(true);

    private readonly ServerOptions _options;

    /// <summary>
    /// 저장 경로 설정을 받아 저장소를 만든다.
    /// options의 DataPath를 사용하며 인스턴스 상태에 보관한다.
    /// </summary>
    public CsvSessionStore(ServerOptions options)
    {
        _options = options;
    }

    /// <summary>세션 CSV가 들어갈 디렉터리 경로.</summary>
    public string SessionsDirectory => Path.Combine(_options.DataPath, "sessions");

    /// <summary>raw gzip이 들어갈 디렉터리 경로.</summary>
    public string RawDirectory => Path.Combine(_options.DataPath, "raw");

    /// <summary>
    /// sessionId가 파일명으로 쓸 수 있는 형태인지 검사한다. path traversal과 제어문자 혼입을 막는다.
    /// sessionId를 입력으로 사용하며 통과하면 true를 반환한다.
    /// </summary>
    public static bool IsValidSessionId(string? sessionId)
    {
        return !string.IsNullOrEmpty(sessionId) && _safeSessionId.IsMatch(sessionId);
    }

    /// <summary>
    /// 세션 CSV를 헤더 행 + 전체 행으로 통째로 교체 기록한다(멱등).
    /// batch의 sessionId와 entries를 사용하며, 기존 파일이 있으면 true를 반환해 교체 사실을 알린다.
    /// </summary>
    public bool SaveSession(UploadBatch batch)
    {
        string path = Path.Combine(SessionsDirectory, batch.SessionId + ".csv");
        SemaphoreSlim gate = FileLocks.Get(path);
        gate.Wait();
        try
        {
            bool replaced = File.Exists(path);
            StringBuilder builder = new StringBuilder();
            builder.Append(SESSION_HEADER).Append(CRLF);

            LogEntry[] entries = batch.Entries ?? System.Array.Empty<LogEntry>();
            foreach (LogEntry entry in entries)
            {
                builder.Append(FormatTimestamp(entry.TimestampUtc)).Append(',');
                builder.Append(EscapeCsvField(NormalizeField(entry.Level, MAX_SHORT_CHARS))).Append(',');
                builder.Append(EscapeCsvField(NormalizeField(entry.Category, MAX_SHORT_CHARS))).Append(',');
                builder.Append(EscapeCsvField(NormalizeField(entry.Scene, MAX_SHORT_CHARS))).Append(',');
                builder.Append(EscapeCsvField(NormalizeField(entry.Message, MAX_MESSAGE_CHARS))).Append(',');
                builder.Append(EscapeCsvField(NormalizeField(entry.DataJson, MAX_DATA_CHARS))).Append(',');
                builder.Append(EscapeCsvField(NormalizeField(entry.StackTrace, MAX_STACK_CHARS))).Append(CRLF);
            }

            WriteAtomicText(path, builder.ToString());
            return replaced;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 수신 원문을 data/raw/{sessionId}.jsonl.gz로 저장한다. KeepRawJsonl이 false면 아무것도 하지 않는다.
    /// sessionId와 수신 본문 bytes를 사용하며, 본문이 gzip이 아니면 압축해서 기록한다.
    /// </summary>
    public void SaveRaw(string sessionId, byte[] body, bool bodyIsGzip)
    {
        if (!_options.KeepRawJsonl)
        {
            return;
        }

        string path = Path.Combine(RawDirectory, sessionId + ".jsonl.gz");
        SemaphoreSlim gate = FileLocks.Get(path);
        gate.Wait();
        try
        {
            byte[] compressed = bodyIsGzip ? body : GzipCompress(body);
            WriteAtomicBytes(path, compressed);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// data 아래 sessions, summary, raw 디렉터리를 없으면 만든다.
    /// 입력값은 없으며 파일 시스템에 디렉터리를 추가로 반영한다.
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(SessionsDirectory);
        Directory.CreateDirectory(Path.Combine(_options.DataPath, "summary"));
        Directory.CreateDirectory(RawDirectory);
    }

    /// <summary>
    /// RawRetentionDays를 넘긴 raw gzip 파일을 삭제한다.
    /// options.RawRetentionDays를 기준 삼아 data/raw의 마지막 수정 시각이 지난 파일을 지우고 삭제 개수를 반환한다.
    /// </summary>
    public int DeleteExpiredRawFiles()
    {
        if (!_options.KeepRawJsonl || _options.RawRetentionDays <= 0 || !Directory.Exists(RawDirectory))
        {
            return 0;
        }

        DateTime deadline = DateTime.UtcNow.AddDays(-_options.RawRetentionDays);
        int deleted = 0;
        foreach (string file in Directory.GetFiles(RawDirectory, "*.gz"))
        {
            if (File.GetLastWriteTimeUtc(file) >= deadline)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"raw delete failed: {file} ({ex.Message})");
            }
        }

        return deleted;
    }

    /// <summary>
    /// RFC 4180 규칙으로 CSV 필드를 감싼다. 쉼표·따옴표·개행이 있으면 쌍따옴표로 감추고 내부 따옴표는 두 번 쓴다.
    /// value를 입력으로 사용하며 이스케이프된 문자열을 반환한다.
    /// </summary>
    public static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// CR, CRLF 개행을 LF로 통일한다.
    /// value를 입력으로 사용하며 개행이 정규화된 문자열을 반환한다.
    /// </summary>
    public static string NormalizeNewlines(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// 필드의 개행을 통일하고 maxChars를 넘는 뒤쪽을 버린다.
    /// value와 maxChars를 사용하며 절단된 문자열을 반환한다.
    /// </summary>
    private static string NormalizeField(string? value, int maxChars)
    {
        string normalized = NormalizeNewlines(value);
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars];
    }

    /// <summary>
    /// ISO 8601 시각 문자열을 Excel에서 인식하기 좋은 yyyy-MM-dd HH:mm:ss.fff로 바꾼다.
    /// timestamp를 입력으로 사용하며 파싱 실패 시 원문 문자열을 그대로 반환한다.
    /// </summary>
    private static string FormatTimestamp(string? timestamp)
    {
        if (TryParseUtc(timestamp, out DateTime value))
        {
            return value.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture);
        }

        return NormalizeNewlines(timestamp).Trim();
    }

    /// <summary>
    /// ISO 8601 문자열을 UTC DateTime으로 해석한다.
    /// text를 입력으로 사용하며 성공 여부와 해석 결과를 반환한다.
    /// </summary>
    internal static bool TryParseUtc(string? text, out DateTime value)
    {
        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out value);
    }

    /// <summary>
    /// 바이트 배열을 gzip으로 압축한다.
    /// raw를 입력으로 사용하며 압축된 바이트 배열을 반환한다.
    /// </summary>
    private static byte[] GzipCompress(byte[] raw)
    {
        using MemoryStream buffer = new();
        using (GZipStream gzip = new(buffer, CompressionLevel.Optimal, true))
        {
            gzip.Write(raw, 0, raw.Length);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// 텍스트를 UTF-8 BOM으로 임시 파일에 쓴 뒤 덮어써서 중간에 잘린 CSV를 막는다.
    /// path와 content를 사용하며 대상 파일 내용을 교체한다.
    /// </summary>
    private static void WriteAtomicText(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content, _utf8Bom);
        File.Move(temp, path, true);
    }

    /// <summary>
    /// 바이트 배열을 임시 파일에 쓴 뒤 덮어써서 잘린 raw 파일을 막는다.
    /// path와 bytes를 사용하며 대상 파일 내용을 교체한다.
    /// </summary>
    private static void WriteAtomicBytes(string path, byte[] bytes)
    {
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, true);
    }
}
