namespace LogServer;

/// <summary>
/// 세션 CSV를 로그 항목 목록으로 읽고 조회 조건을 적용하는 질의 헬퍼.
/// 대시보드와 조회 API가 항목 파싱과 필터링에 사용한다.
/// </summary>
internal static class SessionQuery
{
    private const int ENTRY_COLUMN_COUNT = 7;

    /// <summary>상세 조회 한 번에 돌려줄 수 있는 최대 항목 수.</summary>
    public const int MaxLimit = 2000;

    /// <summary>상세 조회의 기본 항목 수.</summary>
    public const int DefaultLimit = 500;

    /// <summary>
    /// 세션 CSV 텍스트를 로그 항목 목록으로 읽어 들인다. 첫 행은 헤더로 건너뛴다.
    /// csvText를 입력으로 사용하며 열 수가 모자란 행은 버린 항목 목록을 반환한다.
    /// </summary>
    public static List<LogEntry> ParseEntries(string csvText)
    {
        List<LogEntry> entries = new();
        List<string[]> rows = CsvText.ParseRows(csvText);
        for (int i = 1; i < rows.Count; i++)
        {
            string[] cells = rows[i];
            if (cells.Length < ENTRY_COLUMN_COUNT)
            {
                continue;
            }

            entries.Add(new LogEntry
            {
                TimestampUtc = cells[0],
                Level = cells[1],
                Category = cells[2],
                Scene = cells[3],
                Message = cells[4],
                DataJson = cells[5],
                StackTrace = cells[6],
            });
        }

        return entries;
    }

    /// <summary>
    /// 레벨·카테고리·씬·텍스트 조건으로 항목을 골라 offset 위치부터 limit만큼 자른다.
    /// entries와 levels(비면 전체 허용), category, scene, q, offset, limit을 입력으로 사용하며
    /// 조건을 통과한 전체 건수와 페이지 목록을 반환한다.
    /// </summary>
    public static (int Matched, List<LogEntry> Page) Filter(
        List<LogEntry> entries,
        HashSet<string> levels,
        string category,
        string scene,
        string q,
        int offset,
        int limit)
    {
        List<LogEntry> page = new(Math.Min(limit, 512));
        int matched = 0;

        foreach (LogEntry entry in entries)
        {
            if (levels.Count > 0 && !levels.Contains(entry.Level))
            {
                continue;
            }

            if (category.Length > 0 && !string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (scene.Length > 0 && !string.Equals(entry.Scene, scene, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (q.Length > 0
                && !entry.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !entry.DataJson.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !entry.StackTrace.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (matched >= offset && page.Count < limit)
            {
                page.Add(entry);
            }

            matched++;
        }

        return (matched, page);
    }
}
