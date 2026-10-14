using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
    private const string METRICS_HEADER = "metric_key,count,sum";
    private const string CRLF = "\r\n";
    private const int MAX_MESSAGE_CHARS = 2000;
    private const int MAX_STACK_CHARS = 8000;
    private const int MAX_DATA_CHARS = 4000;
    private const int MAX_SHORT_CHARS = 256;
    private const string TIMESTAMP_FORMAT = "yyyy-MM-dd HH:mm:ss.fff";

    private static readonly Regex _safeSessionId = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex _partFilePattern = new(@"^" + FILE_BASE_CAPTURE + @"\.p(\d{4})\.csv$", RegexOptions.Compiled);
    private static readonly Regex _rawPartFilePattern = new(@"^" + FILE_BASE_CAPTURE + @"\.p(\d{4})\.jsonl\.gz$", RegexOptions.Compiled);
    private static readonly UTF8Encoding _utf8Bom = new(true);

    /// <summary>파일명에서 sessionId를 뽑는 정규식 공통 접두.</summary>
    private const string FILE_BASE_CAPTURE = "(?<id>[A-Za-z0-9][A-Za-z0-9._-]{0,63})";

    /// <summary>메타·지표 파일 직렬화에 쓰는 JSON 옵션.</summary>
    internal static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

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

    /// <summary>세션 CSV 파일의 전체 경로. 파트를 병합한 통합 파일이다.</summary>
    public string SessionCsvPath(string sessionId) => Path.Combine(SessionsDirectory, sessionId + ".csv");

    /// <summary>raw gzip 파일의 전체 경로. 구버전 단일 파일 호환용이다.</summary>
    public string RawFilePath(string sessionId) => Path.Combine(RawDirectory, sessionId + ".jsonl.gz");

    /// <summary>세션 메타 JSON 파일의 전체 경로.</summary>
    public string MetaPath(string sessionId) => Path.Combine(SessionsDirectory, sessionId + ".meta.json");

    /// <summary>세션 지표 CSV 파일의 전체 경로.</summary>
    public string MetricsCsvPath(string sessionId) => Path.Combine(SessionsDirectory, sessionId + ".metrics.csv");

    /// <summary>파트 번호 CSV의 전체 경로.</summary>
    public string PartCsvPath(string sessionId, int partIndex) => Path.Combine(SessionsDirectory, $"{sessionId}.p{partIndex:D4}.csv");

    /// <summary>파트 번호 raw gzip의 전체 경로.</summary>
    public string RawPartPath(string sessionId, int partIndex) => Path.Combine(RawDirectory, $"{sessionId}.p{partIndex:D4}.jsonl.gz");

    /// <summary>
    /// 파일명 목록을 파트 번호 오름차순으로 정렬해 돌려준다.
    /// 파일명과 파트 번호 정규식을 사용하며, 번호를 못 읽은 파일은 맨 뒤로 보낸다.
    /// </summary>
    private static List<(int Part, string Path)> SortPartFiles(IEnumerable<string> filePaths, Regex pattern)
    {
        List<(int Part, string Path)> parts = new();
        foreach (string filePath in filePaths)
        {
            Match match = pattern.Match(Path.GetFileName(filePath));
            parts.Add(match.Success ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), filePath) : (int.MaxValue, filePath));
        }

        parts.Sort((a, b) => a.Part.CompareTo(b.Part));
        return parts;
    }

    /// <summary>
    /// sessionId가 파일명으로 쓸 수 있는 형태인지 검사한다. path traversal과 제어문자 혼입을 막는다.
    /// sessionId를 입력으로 사용하며 통과하면 true를 반환한다.
    /// </summary>
    public static bool IsValidSessionId(string? sessionId)
    {
        return !string.IsNullOrEmpty(sessionId) && _safeSessionId.IsMatch(sessionId);
    }

    /// <summary>
    /// 세션 CSV가 디스크에 있는지 확인한다.
    /// sessionId를 입력으로 사용하며 있으면 true를 반환한다.
    /// </summary>
    public bool SessionExists(string sessionId) => File.Exists(SessionCsvPath(sessionId));

    /// <summary>
    /// 세션 CSV 전문을 읽어 들인다. 다른 스레드의 교체 쓰기와 충돌하지 않도록 공유 읽기로 연다.
    /// sessionId를 입력으로 사용하며 파일이 없으면 null을 반환한다.
    /// </summary>
    public string? ReadSessionCsv(string sessionId)
    {
        string path = SessionCsvPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 업로드 배치를 파트로 저장하고 통합 파일을 다시 만든다.
    /// batch의 sessionId와 partIndex, entries, metrics와 수신 원문 rawBody를 사용하며, 세션 단위 잠금 안에서
    /// 메타 JSON, 파트 CSV, 지표 CSV, 파트 raw gzip 기록과 통합 CSV 재구성을 수행한다.
    /// 같은 파트 재전송은 교체로 멱등 처리되며, 기존 파트였는지와 보유 파트 수를 반환한다.
    /// </summary>
    public (bool Replaced, int KnownParts) SavePart(UploadBatch batch, byte[]? rawBody, bool rawBodyIsGzip)
    {
        // 세션 전체를 한 번에 직렬화하기 위해 메타 파일 경로를 세션 잠금으로 쓴다.
        SemaphoreSlim gate = FileLocks.Get(MetaPath(batch.SessionId));
        gate.Wait();
        try
        {
            bool replaced = File.Exists(PartCsvPath(batch.SessionId, batch.PartIndex));
            WriteMetaFile(batch);
            WritePartCsv(batch);
            WriteMetricsCsv(batch);
            WriteRawPart(batch, rawBody, rawBodyIsGzip);
            RebuildMergedCsv(batch.SessionId);
            return (replaced, CountPartFiles(batch.SessionId));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>세션 헤더 정보를 메타 JSON으로 교체 기록한다. 마지막에 도착한 파트의 값이 남는다.</summary>
    private void WriteMetaFile(UploadBatch batch)
    {
        SessionMeta meta = new SessionMeta
        {
            SessionId = batch.SessionId,
            StartedAtUtc = batch.StartedAtUtc,
            EndedAtUtc = batch.EndedAtUtc,
            AppVersion = batch.AppVersion,
            UnityVersion = batch.UnityVersion,
            IsEditor = batch.IsEditor,
            Platform = batch.Platform,
            EndedCleanly = batch.EndedCleanly,
            Truncated = batch.Truncated,
        };
        WriteAtomicText(MetaPath(batch.SessionId), JsonSerializer.Serialize(meta, JsonWeb));
    }

    /// <summary>파트 항목을 헤더 없는 CSV 행들로 교체 기록한다. 통합 CSV의 행 형식과 같다.</summary>
    private void WritePartCsv(UploadBatch batch)
    {
        StringBuilder builder = new StringBuilder();
        foreach (LogEntry entry in batch.Entries ?? System.Array.Empty<LogEntry>())
        {
            builder.Append(BuildEntryRow(entry)).Append(CRLF);
        }

        WriteAtomicText(PartCsvPath(batch.SessionId, batch.PartIndex), builder.ToString());
    }

    /// <summary>세션 지표를 CSV로 교체 기록한다. 샘플이 비어 있으면 기존 지표 파일을 유지한다.</summary>
    private void WriteMetricsCsv(UploadBatch batch)
    {
        MetricSample[] metrics = batch.Metrics ?? System.Array.Empty<MetricSample>();
        if (metrics.Length == 0)
        {
            return;
        }

        StringBuilder builder = new StringBuilder();
        builder.Append(METRICS_HEADER).Append(CRLF);
        foreach (MetricSample sample in metrics)
        {
            builder.Append(EscapeCsvField(sample.Key ?? string.Empty)).Append(',');
            builder.Append(sample.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(sample.Sum.ToString("R", CultureInfo.InvariantCulture)).Append(CRLF);
        }

        WriteAtomicText(MetricsCsvPath(batch.SessionId), builder.ToString());
    }

    /// <summary>수신 원문을 파트 번호 raw gzip으로 교체 기록한다. KeepRawJsonl이 false면 아무것도 하지 않는다.</summary>
    private void WriteRawPart(UploadBatch batch, byte[]? rawBody, bool rawBodyIsGzip)
    {
        if (!_options.KeepRawJsonl || rawBody is null)
        {
            return;
        }

        byte[] compressed = rawBodyIsGzip ? rawBody : GzipCompress(rawBody);
        WriteAtomicBytes(RawPartPath(batch.SessionId, batch.PartIndex), compressed);
    }

    /// <summary>파트 CSV를 번호순으로 이어 붙여 통합 세션 CSV를 교체 기록한다.</summary>
    private void RebuildMergedCsv(string sessionId)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(SESSION_HEADER).Append(CRLF);
        if (Directory.Exists(SessionsDirectory))
        {
            foreach ((int _, string partFile) in SortPartFiles(
                Directory.GetFiles(SessionsDirectory, sessionId + ".p*.csv"), _partFilePattern))
            {
                builder.Append(File.ReadAllText(partFile));
            }
        }

        WriteAtomicText(SessionCsvPath(sessionId), builder.ToString());
    }

    /// <summary>세션이 보유한 파트 CSV 수를 셀수한다.</summary>
    private int CountPartFiles(string sessionId)
    {
        return Directory.Exists(SessionsDirectory)
            ? Directory.GetFiles(SessionsDirectory, sessionId + ".p*.csv").Length
            : 0;
    }

    /// <summary>
    /// 세션 메타 JSON을 읽어 들인다.
    /// sessionId를 입력으로 사용하며 파일이 없거나 파싱에 실패하면 null을 반환한다.
    /// </summary>
    public SessionMeta? ReadMeta(string sessionId)
    {
        string path = MetaPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionMeta>(File.ReadAllText(path), JsonWeb);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 세션 지표 CSV를 읽어 샘플 목록으로 돌려준다.
    /// sessionId를 입력으로 사용하며 파일이 없으면 빈 목록을 반환한다.
    /// </summary>
    public List<MetricSample> ReadMetrics(string sessionId)
    {
        string path = MetricsCsvPath(sessionId);
        List<MetricSample> metrics = new();
        if (!File.Exists(path))
        {
            return metrics;
        }

        List<string[]> rows = CsvText.ParseRows(File.ReadAllText(path));
        for (int i = 1; i < rows.Count; i++)
        {
            string[] cells = rows[i];
            if (cells.Length < 3)
            {
                continue;
            }

            metrics.Add(new MetricSample
            {
                Key = cells[0],
                Count = long.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long count) ? count : 0,
                Sum = MetricMath.ParseSum(cells[2]),
            });
        }

        return metrics;
    }

    /// <summary>
    /// 세션 raw gzip을 파트 번호순으로 이어 붙여 돌려준다(다중 멤버 gzip).
    /// sessionId를 입력으로 사용하며 파트가 없으면 구버전 단일 파일을, 그것도 없으면 null을 반환한다.
    /// </summary>
    public byte[]? ReadRawMerged(string sessionId)
    {
        List<(int Part, string Path)> rawParts = Directory.Exists(RawDirectory)
            ? SortPartFiles(Directory.GetFiles(RawDirectory, sessionId + ".p*.jsonl.gz"), _rawPartFilePattern)
            : new List<(int, string)>();

        if (rawParts.Count == 0)
        {
            string legacyPath = RawFilePath(sessionId);
            return File.Exists(legacyPath) ? File.ReadAllBytes(legacyPath) : null;
        }

        using MemoryStream merged = new();
        foreach ((int _, string partFile) in rawParts)
        {
            byte[] bytes = File.ReadAllBytes(partFile);
            merged.Write(bytes, 0, bytes.Length);
        }

        return merged.ToArray();
    }

    /// <summary>
    /// 세션 CSV와 raw gzip 파일을 삭제한다.
    /// sessionId를 입력으로 사용하며 각각 삭제했는지 담은 튜플을 반환한다.
    /// </summary>
    public (bool SessionCsv, bool Raw) DeleteSessionFiles(string sessionId)
    {
        bool csvDeleted = false;
        bool rawDeleted = false;

        // 세션 단위 잠금으로 저장 중인 파트와의 경합을 막는다.
        SemaphoreSlim gate = FileLocks.Get(MetaPath(sessionId));
        gate.Wait();
        try
        {
            csvDeleted |= DeleteLocked(MetaPath(sessionId));
            csvDeleted |= DeleteLocked(MetricsCsvPath(sessionId));
            csvDeleted |= DeleteLocked(SessionCsvPath(sessionId));
            if (Directory.Exists(SessionsDirectory))
            {
                foreach (string partFile in Directory.GetFiles(SessionsDirectory, sessionId + ".p*.csv"))
                {
                    csvDeleted |= DeleteLocked(partFile);
                }
            }

            if (Directory.Exists(RawDirectory))
            {
                foreach (string rawPartFile in Directory.GetFiles(RawDirectory, sessionId + ".p*.jsonl.gz"))
                {
                    rawDeleted |= DeleteLocked(rawPartFile);
                }
            }

            rawDeleted |= DeleteLocked(RawFilePath(sessionId));
        }
        finally
        {
            gate.Release();
        }

        return (csvDeleted, rawDeleted);
    }

    /// <summary>
    /// 파일을 파일 잠금과 함께 삭제한다.
    /// path를 입력으로 사용하며 삭제했으면 true를, 파일이 없으면 false를 반환한다.
    /// </summary>
    private static bool DeleteLocked(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        SemaphoreSlim gate = FileLocks.Get(path);
        gate.Wait();
        try
        {
            File.Delete(path);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 로그 항목 하나를 통합 CSV 한 행 문자열로 바꾼다.
    /// entry를 입력으로 사용하며 타임스탬프 포매팅과 길이 절단, CSV 이스케이프가 적용된 행을 반환한다.
    /// </summary>
    public static string BuildEntryRow(LogEntry entry)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(FormatTimestamp(entry.TimestampUtc)).Append(',');
        builder.Append(EscapeCsvField(NormalizeField(entry.Level, MAX_SHORT_CHARS))).Append(',');
        builder.Append(EscapeCsvField(NormalizeField(entry.Category, MAX_SHORT_CHARS))).Append(',');
        builder.Append(EscapeCsvField(NormalizeField(entry.Scene, MAX_SHORT_CHARS))).Append(',');
        builder.Append(EscapeCsvField(NormalizeField(entry.Message, MAX_MESSAGE_CHARS))).Append(',');
        builder.Append(EscapeCsvField(NormalizeField(entry.DataJson, MAX_DATA_CHARS))).Append(',');
        builder.Append(EscapeCsvField(NormalizeField(entry.StackTrace, MAX_STACK_CHARS)));
        return builder.ToString();
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
