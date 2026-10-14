using System.Globalization;
using System.Text;

namespace LogServer;

/// <summary>
/// 세션 요약을 집계해 data/summary/{yyyy-MM}.csv의 sessionId 행을 교체(upsert)하는 집계기.
/// UploadBatch를 받아 레벨별 카운트와 세션 지속 시간을 계산하고 월별 요약 파일 상태를 변경한다.
/// </summary>
public sealed class SessionSummary
{
    private const string SUMMARY_HEADER = "session_id,date_utc,duration_sec,ended_cleanly,total,warnings,errors,exceptions,is_editor,platform,app_version";
    private const int COLUMN_COUNT = 11;
    private const string CRLF = "\r\n";
    private const string DATE_FORMAT = "yyyy-MM-dd HH:mm:ss";

    private static readonly UTF8Encoding _utf8Bom = new(true);

    private readonly ServerOptions _options;

    /// <summary>
    /// 요약 저장 경로 설정을 받아 집계기를 만든다.
    /// options의 DataPath를 사용하며 인스턴스 상태에 보관한다.
    /// </summary>
    public SessionSummary(ServerOptions options)
    {
        _options = options;
    }

    /// <summary>월별 요약 CSV가 들어갈 디렉터리 경로.</summary>
    public string SummaryDirectory => Path.Combine(_options.DataPath, "summary");

    /// <summary>
    /// 수신 배치의 정제 요약 수치를 계산한다.
    /// batch의 시각과 entries를 사용해 기간·레벨별 건수·최다 카테고리를 담은 UploadSummary를 반환한다.
    /// </summary>
    public UploadSummary Describe(UploadBatch batch)
    {
        bool hasStart = CsvSessionStore.TryParseUtc(batch.StartedAtUtc, out DateTime startedAtUtc);
        DateTime reference = hasStart ? startedAtUtc : DateTime.UtcNow;
        bool hasEnd = CsvSessionStore.TryParseUtc(batch.EndedAtUtc, out DateTime endedAtUtc);
        double durationSec = hasStart && hasEnd ? Math.Max(0.0, (endedAtUtc - startedAtUtc).TotalSeconds) : 0.0;
        LogEntry[] entries = batch.Entries ?? System.Array.Empty<LogEntry>();
        CountLevels(entries, out int warnings, out int errors, out int exceptions);

        return new UploadSummary
        {
            Month = reference.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            DateUtc = reference.ToString(DATE_FORMAT, CultureInfo.InvariantCulture),
            DurationSec = Math.Round(durationSec, 1),
            EndedCleanly = batch.EndedCleanly,
            Total = entries.Length,
            Warnings = warnings,
            Errors = errors,
            Exceptions = exceptions,
            TopCategory = FindTopCategory(entries),
        };
    }

    /// <summary>
    /// 수신한 배치의 요약 행을 계산해 월별 요약 CSV에서 sessionId 키로 교체 기록한다.
    /// batch의 시각과 entries를 사용하며, 행이 쓰인 월 키(yyyy-MM)를 반환한다.
    /// </summary>
    public string Upsert(UploadBatch batch)
    {
        UploadSummary info = Describe(batch);
        string path = Path.Combine(SummaryDirectory, info.Month + ".csv");

        string[] row = new string[COLUMN_COUNT]
        {
            batch.SessionId,
            info.DateUtc,
            info.DurationSec.ToString("0.0", CultureInfo.InvariantCulture),
            info.EndedCleanly ? "true" : "false",
            info.Total.ToString(CultureInfo.InvariantCulture),
            info.Warnings.ToString(CultureInfo.InvariantCulture),
            info.Errors.ToString(CultureInfo.InvariantCulture),
            info.Exceptions.ToString(CultureInfo.InvariantCulture),
            batch.IsEditor ? "true" : "false",
            CsvSessionStore.NormalizeNewlines(batch.Platform),
            CsvSessionStore.NormalizeNewlines(batch.AppVersion),
        };

        SemaphoreSlim gate = FileLocks.Get(path);
        gate.Wait();
        try
        {
            List<string[]> rows = File.Exists(path)
                ? ParseRows(File.ReadAllText(path))
                : new List<string[]>();
            if (rows.Count == 0)
            {
                rows.Add(SUMMARY_HEADER.Split(','));
            }

            int existing = IndexOfSession(rows, batch.SessionId);
            if (existing >= 0)
            {
                rows[existing] = row;
            }
            else
            {
                rows.Add(row);
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(SUMMARY_HEADER).Append(CRLF);
            for (int i = 1; i < rows.Count; i++)
            {
                builder.Append(JoinRow(rows[i])).Append(CRLF);
            }

            string temp = path + ".tmp";
            File.WriteAllText(temp, builder.ToString(), _utf8Bom);
            File.Move(temp, path, true);
        }
        finally
        {
            gate.Release();
        }

        return info.Month;
    }

    /// <summary>
    /// 로그 항목 중 가장 많은 카테고리를 찾는다.
    /// entries를 입력으로 사용하며, 동률이면 먼저 나온 카테고리를, 항목이 없으면 빈 문자열을 반환한다.
    /// </summary>
    private static string FindTopCategory(LogEntry[] entries)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        string best = string.Empty;
        int bestCount = 0;

        foreach (LogEntry entry in entries)
        {
            string category = string.IsNullOrEmpty(entry.Category) ? "Unity" : entry.Category;
            int count = counts.TryGetValue(category, out int current) ? current + 1 : 1;
            counts[category] = count;

            if (count > bestCount)
            {
                bestCount = count;
                best = category;
            }
        }

        return best;
    }

    /// <summary>
    /// 로그 항목 목록에서 Warning, Error, Assert, Exception 건수를 센다.
    /// entries를 입력으로 사용하며 세 개 합계를 out 파라미터에 저장한다.
    /// </summary>
    private static void CountLevels(LogEntry[] entries, out int warnings, out int errors, out int exceptions)
    {
        warnings = 0;
        errors = 0;
        exceptions = 0;

        foreach (LogEntry entry in entries)
        {
            if (string.Equals(entry.Level, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                warnings++;
            }
            else if (string.Equals(entry.Level, "Exception", StringComparison.OrdinalIgnoreCase))
            {
                exceptions++;
            }
            else if (string.Equals(entry.Level, "Error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Level, "Assert", StringComparison.OrdinalIgnoreCase))
            {
                errors++;
            }
        }
    }

    /// <summary>
    /// 요약 행 목록에서 sessionId와 일치하는 행의 번호를 찾는다. 헤더 행(0번)은 건너뛴다.
    /// rows와 sessionId를 입력으로 사용하며 없으면 -1을 반환한다.
    /// </summary>
    private static int IndexOfSession(List<string[]> rows, string sessionId)
    {
        for (int i = 1; i < rows.Count; i++)
        {
            if (rows[i].Length > 0 && string.Equals(rows[i][0], sessionId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 요약 행 필드를 RFC 4180 이스케이프로 한 줄 텍스트로 붙인다.
    /// cells를 입력으로 사용하며 COLUMN_COUNT까지 빈 칸을 채운 CSV 한 행을 반환한다.
    /// </summary>
    private static string JoinRow(string[] cells)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < COLUMN_COUNT; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(CsvSessionStore.EscapeCsvField(i < cells.Length ? cells[i] : string.Empty));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 요약 CSV 텍스트를 RFC 4180 규칙으로 읽어 행 목록으로 만든다. 인용 필드 안의 개행과 쉼표를 존중한다.
    /// text를 입력으로 사용하며 비어 있지 않은 행만 담은 목록을 반환한다.
    /// </summary>
    private static List<string[]> ParseRows(string text)
    {
        List<string[]> rows = new();
        List<string> fields = new();
        StringBuilder field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        rows.RemoveAll(row => row.Length == 0 || (row.Length == 1 && row[0].Length == 0));
        return rows;
    }
}
