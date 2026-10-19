using System.Text.Json;

namespace LogServer;

/// <summary>
/// 삭제한 세션을 보관하는 휴지통. 세션 파일(sessions·raw)을 data/trash/{sessionId}/ 아래로 옮기고 요약 행을 trash.json에 남긴다.
/// 복원하면 파일을 제자리로 돌리고 요약 행을 다시 쓴다. TrashRetentionDays가 지난 항목은 PurgeExpired가 영구 삭제한다.
/// </summary>
public sealed class SessionTrash
{
    private const string ItemFile = "trash.json";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ServerOptions _options;
    private readonly CsvSessionStore _store;
    private readonly SessionSummary _summary;
    private readonly object _gate = new();

    public SessionTrash(ServerOptions options, CsvSessionStore store, SessionSummary summary)
    {
        _options = options;
        _store = store;
        _summary = summary;
    }

    /// <summary>휴지통 루트 디렉터리.</summary>
    public string TrashDirectory => Path.Combine(_options.DataPath, "trash");

    private string ItemDirectory(string sessionId) => Path.Combine(TrashDirectory, sessionId);

    /// <summary>
    /// 세션을 휴지통으로 옮기고 요약 행을 지운다. 같은 세션이 이미 휴지통에 있으면 새로 옮긴 것으로 바꾼다.
    /// 옮긴 파일도 요약 행도 없으면 false를 반환한다.
    /// </summary>
    public bool MoveToTrash(string sessionId)
    {
        lock (_gate)
        {
            (string Month, SessionRow Row)? found = _summary.FindSession(sessionId);
            if (found is null && !_store.HasSessionFiles(sessionId))
            {
                return false;
            }

            // trash.json을 먼저 써 두어, 파일을 옮기다 멈춰도 휴지통 항목으로 남아 복원할 수 있게 한다
            string dir = ItemDirectory(sessionId);
            DeleteDirectory(dir);
            Directory.CreateDirectory(dir);
            TrashItem item = new() { SessionId = sessionId, DeletedUtc = DateTime.UtcNow, Row = found?.Row };
            string itemPath = Path.Combine(dir, ItemFile);
            File.WriteAllText(itemPath, JsonSerializer.Serialize(item, _json));
            item.Files = _store.MoveSessionFiles(sessionId, dir);
            File.WriteAllText(itemPath, JsonSerializer.Serialize(item, _json));
            _summary.RemoveSession(sessionId);
            return true;
        }
    }

    /// <summary>휴지통 항목 목록(최근 삭제순). 자동 삭제 예정 시각을 채워 돌려준다.</summary>
    public List<TrashItem> List()
    {
        List<TrashItem> items = new();
        if (!Directory.Exists(TrashDirectory))
        {
            return items;
        }

        foreach (string dir in Directory.GetDirectories(TrashDirectory))
        {
            if (Read(dir) is { } item)
            {
                item.PurgeAtUtc = item.DeletedUtc.AddDays(_options.TrashRetentionDays);
                items.Add(item);
            }
        }

        return items.OrderByDescending(x => x.DeletedUtc).ToList();
    }

    /// <summary>
    /// 휴지통의 세션을 제자리로 돌리고 요약 행을 다시 쓴다. 같은 세션 파일이 이미 있으면(삭제 뒤 다시 업로드됨) 덮어쓰지 않고 Conflict.
    /// </summary>
    public TrashRestoreResult Restore(string sessionId)
    {
        lock (_gate)
        {
            string dir = ItemDirectory(sessionId);
            if (Read(dir) is not { } item)
            {
                return TrashRestoreResult.NotFound;
            }

            if (!_store.RestoreSessionFiles(sessionId, dir))
            {
                return TrashRestoreResult.Conflict;
            }

            if (item.Row != null)
            {
                _summary.RestoreRow(item.Row);
            }
            else
            {
                _summary.UpsertSession(sessionId, _store);
            }

            DeleteDirectory(dir);
            return TrashRestoreResult.Restored;
        }
    }

    /// <summary>휴지통 항목 하나를 영구 삭제한다. 없으면 false.</summary>
    public bool Purge(string sessionId)
    {
        lock (_gate)
        {
            string dir = ItemDirectory(sessionId);
            if (!Directory.Exists(dir))
            {
                return false;
            }

            DeleteDirectory(dir);
            return true;
        }
    }

    /// <summary>휴지통을 비운다. 지운 항목 수를 돌려준다.</summary>
    public int PurgeAll() => PurgeWhere(_ => true);

    /// <summary>삭제한 지 TrashRetentionDays가 지난 항목을 영구 삭제한다. 0 이하면 아무것도 지우지 않는다.</summary>
    public int PurgeExpired()
    {
        if (_options.TrashRetentionDays <= 0)
        {
            return 0;
        }

        DateTime deadline = DateTime.UtcNow.AddDays(-_options.TrashRetentionDays);
        return PurgeWhere(item => item.DeletedUtc <= deadline);
    }

    private int PurgeWhere(Func<TrashItem, bool> match)
    {
        lock (_gate)
        {
            int purged = 0;
            if (!Directory.Exists(TrashDirectory))
            {
                return 0;
            }

            foreach (string dir in Directory.GetDirectories(TrashDirectory))
            {
                // trash.json이 없거나 깨진 폴더는 폴더 생성 시각을 삭제 시각으로 본다
                TrashItem item = Read(dir) ?? new TrashItem { SessionId = Path.GetFileName(dir), DeletedUtc = Directory.GetCreationTimeUtc(dir) };
                if (match(item))
                {
                    DeleteDirectory(dir);
                    purged++;
                }
            }

            return purged;
        }
    }

    private static TrashItem? Read(string dir)
    {
        string path = Path.Combine(dir, ItemFile);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TrashItem>(File.ReadAllText(path), _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>폴더를 지운다. 방금 만든 파일을 백신·색인기가 잠깐 잡고 있으면 실패하므로 몇 번 다시 시도한다.</summary>
    private static void DeleteDirectory(string dir)
    {
        for (int attempt = 1; Directory.Exists(dir); attempt++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }
}

public enum TrashRestoreResult
{
    Restored,
    NotFound,
    Conflict,
}

/// <summary>휴지통 항목 하나. Row는 삭제 직전 요약 행(없으면 null), PurgeAtUtc는 자동 삭제 예정 시각이다.</summary>
public sealed class TrashItem
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime DeletedUtc { get; set; }
    public DateTime PurgeAtUtc { get; set; }
    public int Files { get; set; }
    public SessionRow? Row { get; set; }
}

/// <summary>GET /api/trash 응답 본문.</summary>
public sealed class TrashListResponse
{
    /// <summary>항목 보관 일수(설정 TrashRetentionDays).</summary>
    public int RetentionDays { get; set; }

    public List<TrashItem> Items { get; set; } = new();
}
