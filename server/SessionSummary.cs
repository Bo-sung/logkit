using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

    private static readonly Regex _monthFilePattern = new(@"^(\d{4}-\d{2})\.csv$", RegexOptions.Compiled);

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
    /// 저장된 세션 데이터를 다시 계산해 요약 행과 정제 요약을 만든다.
    /// sessionId와 저장소를 입력으로 사용하며, 메타 JSON과 통합 CSV에서 기간·레벨별 건수·최다 카테고리를 담은
    /// SessionRow와 UploadSummary를 반환한다. 세션 데이터가 없으면 null을 반환한다.
    /// </summary>
    public (SessionRow Row, UploadSummary Info)? ComputeSession(string sessionId, CsvSessionStore store)
    {
        SessionMeta? meta = store.ReadMeta(sessionId);
        List<LogEntry>? parsed = store.ReadSessionEntries(sessionId);
        if (meta is null && parsed is null)
        {
            return null;
        }

        List<LogEntry> entries = parsed ?? new List<LogEntry>();
        bool hasStart = CsvSessionStore.TryParseUtc(meta?.StartedAtUtc, out DateTime startedAtUtc);
        DateTime reference = hasStart ? startedAtUtc : DateTime.UtcNow;
        bool hasEnd = CsvSessionStore.TryParseUtc(meta?.EndedAtUtc, out DateTime endedAtUtc);
        double durationSec = hasStart && hasEnd ? Math.Max(0.0, (endedAtUtc - startedAtUtc).TotalSeconds) : 0.0;
        CountLevels(entries, out int warnings, out int errors, out int exceptions);
        string dateUtc = reference.ToString(DATE_FORMAT, CultureInfo.InvariantCulture);
        double roundedDuration = Math.Round(durationSec, 1);

        SessionRow row = new SessionRow
        {
            SessionId = sessionId,
            DateUtc = dateUtc,
            DurationSec = roundedDuration,
            EndedCleanly = meta?.EndedCleanly ?? false,
            Total = entries.Count,
            Warnings = warnings,
            Errors = errors,
            Exceptions = exceptions,
            IsEditor = meta?.IsEditor ?? false,
            Platform = CsvSessionStore.NormalizeNewlines(meta?.Platform),
            AppVersion = CsvSessionStore.NormalizeNewlines(meta?.AppVersion),
        };

        UploadSummary info = new UploadSummary
        {
            Month = reference.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            DateUtc = dateUtc,
            DurationSec = roundedDuration,
            EndedCleanly = row.EndedCleanly,
            Total = row.Total,
            Warnings = warnings,
            Errors = errors,
            Exceptions = exceptions,
            TopCategory = FindTopCategory(entries),
        };

        return (row, info);
    }

    /// <summary>
    /// 저장된 세션 데이터를 다시 계산해 월별 요약 CSV의 sessionId 행을 교체(upsert)한다.
    /// sessionId와 저장소를 입력으로 사용하며, 계산된 UploadSummary를 반환한다.
    /// 세션이 없으면 null을 반환한다.
    /// </summary>
    public UploadSummary? UpsertSession(string sessionId, CsvSessionStore store)
    {
        (SessionRow Row, UploadSummary Info)? computed = ComputeSession(sessionId, store);
        if (computed is null)
        {
            return null;
        }

        UpsertRow(computed.Value.Row);
        return computed.Value.Info;
    }

    /// <summary>휴지통에서 복원한 세션의 요약 행을 그대로 다시 쓴다.</summary>
    public void RestoreRow(SessionRow row) => UpsertRow(row);

    /// <summary>
    /// 세션 요약 행을 월별 요약 CSV의 sessionId 키로 교체 기록한다.
    /// row를 입력으로 사용하며, 행이 쓰인 월 키(yyyy-MM)를 반환한다.
    /// </summary>
    private void UpsertRow(SessionRow row)
    {
        if (row.DateUtc.Length < 7)
        {
            return;
        }

        string month = row.DateUtc[..7];
        string path = Path.Combine(SummaryDirectory, month + ".csv");

        string[] cells = new string[COLUMN_COUNT]
        {
            row.SessionId,
            row.DateUtc,
            row.DurationSec.ToString("0.0", CultureInfo.InvariantCulture),
            row.EndedCleanly ? "true" : "false",
            row.Total.ToString(CultureInfo.InvariantCulture),
            row.Warnings.ToString(CultureInfo.InvariantCulture),
            row.Errors.ToString(CultureInfo.InvariantCulture),
            row.Exceptions.ToString(CultureInfo.InvariantCulture),
            row.IsEditor ? "true" : "false",
            CsvSessionStore.NormalizeNewlines(row.Platform),
            CsvSessionStore.NormalizeNewlines(row.AppVersion),
        };

        SemaphoreSlim gate = FileLocks.Get(path);
        gate.Wait();
        try
        {
            List<string[]> rows = File.Exists(path)
                ? CsvText.ParseRows(File.ReadAllText(path))
                : new List<string[]>();
            if (rows.Count == 0)
            {
                rows.Add(SUMMARY_HEADER.Split(','));
            }

            int existing = IndexOfSession(rows, row.SessionId);
            if (existing >= 0)
            {
                rows[existing] = cells;
            }
            else
            {
                rows.Add(cells);
            }

            WriteSummaryFile(path, rows);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 요약 파일이 있는 월 목록을 월별 집계와 함께 최신순으로 돌려준다.
    /// 입력값은 없으며 파일명이 월 형식인 요약 파일만 집계해 반환한다.
    /// </summary>
    public List<MonthInfo> ListMonths()
    {
        List<MonthInfo> months = new();
        if (!Directory.Exists(SummaryDirectory))
        {
            return months;
        }

        foreach (string file in Directory.GetFiles(SummaryDirectory, "*.csv"))
        {
            Match match = _monthFilePattern.Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            List<SessionRow> sessions = ReadMonthRows(match.Groups[1].Value);
            MonthInfo info = new() { Month = match.Groups[1].Value, Sessions = sessions.Count };
            foreach (SessionRow session in sessions)
            {
                info.Total += session.Total;
                info.Warnings += session.Warnings;
                info.Errors += session.Errors;
                info.Exceptions += session.Exceptions;
            }

            months.Add(info);
        }

        months.Sort((a, b) => string.CompareOrdinal(b.Month, a.Month));
        return months;
    }

    /// <summary>
    /// 지정한 월의 요약 행을 세션 목록으로 돌려준다.
    /// month(yyyy-MM)를 입력으로 사용하며 시작 시각 내림차순으로 정렬해 반환한다. 파일이 없으면 빈 목록을 반환한다.
    /// </summary>
    public List<SessionRow> GetMonthSessions(string month)
    {
        List<SessionRow> sessions = ReadMonthRows(month);
        sessions.Sort((a, b) => string.CompareOrdinal(b.DateUtc, a.DateUtc));
        return sessions;
    }

    /// <summary>
    /// sessionId가 기록된 요약 행과 그 월을 찾는다.
    /// sessionId를 입력으로 사용하며 찾으면 월과 행을, 없으면 null을 반환한다.
    /// </summary>
    public (string Month, SessionRow Row)? FindSession(string sessionId)
    {
        if (!Directory.Exists(SummaryDirectory))
        {
            return null;
        }

        foreach (string file in Directory.GetFiles(SummaryDirectory, "*.csv"))
        {
            Match match = _monthFilePattern.Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            List<string[]> rows = CsvText.ParseRows(File.ReadAllText(file));
            int index = IndexOfSession(rows, sessionId);
            if (index < 0)
            {
                continue;
            }

            SessionRow? row = ToSessionRow(rows[index]);
            if (row != null)
            {
                return (match.Groups[1].Value, row);
            }
        }

        return null;
    }

    /// <summary>
    /// 모든 월의 요약에서 sessionId 행을 제거한다.
    /// sessionId를 입력으로 사용하며 행을 제거한 월 목록을 반환한다.
    /// </summary>
    public List<string> RemoveSession(string sessionId)
    {
        List<string> months = new();
        if (!Directory.Exists(SummaryDirectory))
        {
            return months;
        }

        foreach (string file in Directory.GetFiles(SummaryDirectory, "*.csv"))
        {
            Match match = _monthFilePattern.Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            SemaphoreSlim gate = FileLocks.Get(file);
            gate.Wait();
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                List<string[]> rows = CsvText.ParseRows(File.ReadAllText(file));
                int index = IndexOfSession(rows, sessionId);
                if (index < 0)
                {
                    continue;
                }

                rows.RemoveAt(index);
                WriteSummaryFile(file, rows);
                months.Add(match.Groups[1].Value);
            }
            finally
            {
                gate.Release();
            }
        }

        return months;
    }

    /// <summary>
    /// 월 요약 파일을 삭제한다.
    /// month(yyyy-MM)를 입력으로 사용하며 삭제했으면 true를 반환한다.
    /// </summary>
    public bool DeleteMonth(string month)
    {
        string path = Path.Combine(SummaryDirectory, month + ".csv");
        if (!File.Exists(path))
        {
            return false;
        }

        SemaphoreSlim gate = FileLocks.Get(path);
        gate.Wait();
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 요약 행 목록을 헤더와 함께 파일로 교체 기록한다.
    /// path와 rows를 사용하며 임시 파일 쓰기 후 교체로 저장한다. rows의 0번은 헤더로 건너뛴다.
    /// </summary>
    private void WriteSummaryFile(string path, List<string[]> rows)
    {
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

    /// <summary>
    /// 월 요약 파일을 읽어 세션 목록으로 변환한다.
    /// month(yyyy-MM)를 입력으로 사용하며 파일이 없으면 빈 목록을 반환한다.
    /// </summary>
    private List<SessionRow> ReadMonthRows(string month)
    {
        string path = Path.Combine(SummaryDirectory, month + ".csv");
        if (!File.Exists(path))
        {
            return new List<SessionRow>();
        }

        List<SessionRow> sessions = new();
        foreach (string[] cells in CsvText.ParseRows(File.ReadAllText(path)))
        {
            SessionRow? row = ToSessionRow(cells);
            if (row != null)
            {
                sessions.Add(row);
            }
        }

        return sessions;
    }

    /// <summary>
    /// 요약 CSV 한 행을 세션 목록 항목으로 변환한다.
    /// cells를 입력으로 사용하며 헤더 행이거나 열 수가 모자라면 null을 반환한다.
    /// </summary>
    private static SessionRow? ToSessionRow(string[] cells)
    {
        if (cells.Length < COLUMN_COUNT || cells[0] == "session_id")
        {
            return null;
        }

        return new SessionRow
        {
            SessionId = cells[0],
            DateUtc = cells[1],
            DurationSec = ParseDouble(cells[2]),
            EndedCleanly = string.Equals(cells[3], "true", StringComparison.OrdinalIgnoreCase),
            Total = ParseInt(cells[4]),
            Warnings = ParseInt(cells[5]),
            Errors = ParseInt(cells[6]),
            Exceptions = ParseInt(cells[7]),
            IsEditor = string.Equals(cells[8], "true", StringComparison.OrdinalIgnoreCase),
            Platform = cells[9],
            AppVersion = cells[10],
        };
    }

    /// <summary>정수 필드를 관대하게 읽는다. 형식이 틀리면 0을 반환한다.</summary>
    private static int ParseInt(string text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    /// <summary>실수 필드를 관대하게 읽는다. 형식이 틀리면 0을 반환한다.</summary>
    private static double ParseDouble(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;
    }

    /// <summary>
    /// 로그 항목 중 가장 많은 카테고리를 찾는다.
    /// entries를 입력으로 사용하며, 동률이면 먼저 나온 카테고리를, 항목이 없으면 빈 문자열을 반환한다.
    /// </summary>
    private static string FindTopCategory(List<LogEntry> entries)
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
    private static void CountLevels(List<LogEntry> entries, out int warnings, out int errors, out int exceptions)
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
}
