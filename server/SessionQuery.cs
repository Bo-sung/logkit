namespace LogServer;

/// <summary>
/// GM 툴 세션 상세용 파싱 결과 캐시. 페이지를 넘기거나 필터를 바꿀 때마다 큰 세션 CSV(100MB 이상)를 다시 파싱하지 않도록
/// 최근에 연 세션 몇 개의 항목 목록을 CSV 수정 시각과 함께 들고 있다. 수정 시각이 바뀌면(새 업로드) 다시 읽는다.
/// </summary>
public sealed class SessionEntryCache
{
    private const int CAPACITY = 3;

    private readonly CsvSessionStore _store;
    private readonly LinkedList<(string SessionId, DateTime WriteTime, List<LogEntry> Entries)> _recent = new();
    private readonly object _lock = new();

    public SessionEntryCache(CsvSessionStore store)
    {
        _store = store;
    }

    /// <summary>세션의 로그 항목 목록. CSV가 없으면 null. 돌려준 목록은 읽기만 한다.</summary>
    public List<LogEntry>? Get(string sessionId)
    {
        string path = _store.SessionCsvPath(sessionId);
        if (!File.Exists(path))
        {
            Forget(sessionId);
            return null;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(path);
        lock (_lock)
        {
            for (LinkedListNode<(string SessionId, DateTime WriteTime, List<LogEntry> Entries)>? node = _recent.First; node != null; node = node.Next)
            {
                if (node.Value.SessionId == sessionId && node.Value.WriteTime == writeTime)
                {
                    _recent.Remove(node);
                    _recent.AddFirst(node);
                    return node.Value.Entries;
                }
            }
        }

        List<LogEntry>? entries = _store.ReadSessionEntries(sessionId);
        if (entries == null)
        {
            return null;
        }

        lock (_lock)
        {
            Remove(sessionId);
            _recent.AddFirst((sessionId, writeTime, entries));
            while (_recent.Count > CAPACITY)
            {
                _recent.RemoveLast();
            }
        }

        return entries;
    }

    private void Forget(string sessionId)
    {
        lock (_lock)
        {
            Remove(sessionId);
        }
    }

    private void Remove(string sessionId)
    {
        for (LinkedListNode<(string SessionId, DateTime WriteTime, List<LogEntry> Entries)>? node = _recent.First; node != null; node = node.Next)
        {
            if (node.Value.SessionId == sessionId)
            {
                _recent.Remove(node);
                return;
            }
        }
    }
}

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
    public static List<LogEntry> ParseEntries(string csvText) => ParseEntries(new StringReader(csvText));

    /// <summary>
    /// 세션 CSV를 읽기 도구에서 한 행씩 읽어 로그 항목 목록으로 만든다. 첫 행은 헤더로 건너뛴다.
    /// 레벨·카테고리·씬·메시지·스택은 반복이 많아 같은 문자열 하나를 함께 쓴다(큰 세션의 메모리 절약).
    /// </summary>
    public static List<LogEntry> ParseEntries(TextReader reader)
    {
        List<LogEntry> entries = new();
        Dictionary<string, string> pool = new(StringComparer.Ordinal);
        string Shared(string text)
        {
            if (text.Length > 256)
            {
                return text;
            }

            if (pool.TryGetValue(text, out string? same))
            {
                return same;
            }

            pool[text] = text;
            return text;
        }

        bool header = true;
        foreach (string[] cells in CsvText.ReadRows(reader))
        {
            if (header)
            {
                header = false;
                continue;
            }

            if (cells.Length < ENTRY_COLUMN_COUNT)
            {
                continue;
            }

            entries.Add(new LogEntry
            {
                TimestampUtc = cells[0],
                Level = Shared(cells[1]),
                Category = Shared(cells[2]),
                Scene = Shared(cells[3]),
                Message = Shared(cells[4]),
                DataJson = cells[5],
                StackTrace = Shared(cells[6]),
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
