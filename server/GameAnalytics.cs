using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace LogServer;

/// <summary>
/// 게임 이벤트 로그 규약. 클라이언트는 GameLogger.Data(level, 카테고리, 이벤트 이름, 데이터)로 기록하고
/// 분석기는 category와 message(이벤트 이름)로 이벤트를 식별해 dataJson 필드를 읽는다.
/// 기록 시각은 항목의 timestampUtc를 그대로 쓰므로 데이터에 시간 필드를 따로 넣지 않는다.
/// </summary>
internal static class GameEvents
{
    public const string ShopCategory = "Shop";
    public const string RaidCategory = "Raid";

    // 상점 운영
    public const string ShopStart = "ShopStart";               // day, money
    public const string WorkbenchEnter = "WorkbenchEnter";     // orderId
    public const string WorkbenchExit = "WorkbenchExit";       // orderId, gunCode
    public const string CustomerInteract = "CustomerInteract"; // orderId
    public const string OrderComplete = "OrderComplete";       // orderId, success, died, gunCode(선택)
    public const string PresetSave = "PresetSave";             // gunCode
    public const string Fire = "Fire";                         // orderId, gunCode
    public const string ShopEnd = "ShopEnd";                   // day, money

    // 야간 습격
    public const string RaidStart = "RaidStart";               // stageId, guns[], modules[](선택)
    public const string RaidSample = "RaidSample";             // x, y, z, yaw, pitch, hp, vaults, tanks, helis
    public const string BlockDestroyed = "BlockDestroyed";     // stageId, blockId
    public const string EnemySpawn = "EnemySpawn";             // enemyId
    public const string EnemySpotted = "EnemySpotted";         // enemyId
    public const string EnemyKilled = "EnemyKilled";           // enemyId
    public const string EscapeSuccess = "EscapeSuccess";       // playTime, vaultReward, kills, hp
}

/// <summary>
/// 버전별 플레이 분석 집계기. 세션 CSV를 이벤트 규약에 따라 세션 요약(digest)으로 줄이고,
/// 요약을 버전 단위로 합산해 대시보드 차트용 응답을 만든다.
/// 세션 요약은 CSV 수정 시각 기준으로 캐시해 같은 세션을 매 요청마다 다시 파싱하지 않는다.
/// </summary>
public sealed class GameAnalytics
{
    /// <summary>주문당 사격 횟수 분포의 구간 수. 마지막 구간은 그 이상을 모두 담는다.</summary>
    public const int ShotBuckets = 11;

    /// <summary>습격 타임라인 구간 길이(초).</summary>
    public const int TimelineBinSeconds = 10;

    private const int MAX_TIMELINE_BINS = 360;
    private const int HEATMAP_GRID = 64;
    private const int TOP_LIST_SIZE = 15;
    private const string UNKNOWN = "(미지정)";

    private readonly CsvSessionStore _store;
    private readonly SessionSummary _summary;
    private readonly ConcurrentDictionary<string, (DateTime WriteTime, SessionDigest Digest)> _cache = new(StringComparer.Ordinal);

    /// <summary>세션 저장소와 요약 집계기를 받아 분석기를 만든다.</summary>
    public GameAnalytics(CsvSessionStore store, SessionSummary summary)
    {
        _store = store;
        _summary = summary;
    }

    /// <summary>
    /// 범위 안의 세션을 버전별로 집계한다.
    /// month(yyyy-MM, 비면 전체 월)와 excludeEditor를 사용하며, 버전 오름차순 집계 응답을 반환한다.
    /// </summary>
    public AnalyticsResponse Build(string? month, bool excludeEditor)
    {
        List<string> months = string.IsNullOrEmpty(month)
            ? _summary.ListMonths().Select(info => info.Month).ToList()
            : new List<string> { month };

        Dictionary<string, List<SessionDigest>> byVersion = new(StringComparer.Ordinal);
        foreach (string m in months)
        {
            foreach (SessionRow row in _summary.GetMonthSessions(m))
            {
                if (excludeEditor && row.IsEditor)
                {
                    continue;
                }

                SessionDigest? digest = GetDigest(row.SessionId);
                if (digest == null)
                {
                    continue;
                }

                string version = string.IsNullOrEmpty(row.AppVersion) ? UNKNOWN : row.AppVersion;
                if (!byVersion.TryGetValue(version, out List<SessionDigest>? list))
                {
                    list = new List<SessionDigest>();
                    byVersion[version] = list;
                }

                list.Add(digest);
            }
        }

        Dictionary<string, HeatmapBounds> bounds = ComputeHeatmapBounds(byVersion.Values.SelectMany(list => list));
        List<string> versions = byVersion.Keys.ToList();
        versions.Sort(CompareVersions);

        return new AnalyticsResponse
        {
            Month = month ?? string.Empty,
            ExcludeEditor = excludeEditor,
            TimelineBinSeconds = TimelineBinSeconds,
            Versions = versions.Select(v => Aggregate(v, byVersion[v], bounds)).ToList(),
        };
    }

    /// <summary>
    /// 세션 요약을 캐시에서 꺼내거나 CSV를 읽어 새로 만든다.
    /// sessionId를 사용하며 CSV가 없으면 null을 반환한다.
    /// </summary>
    private SessionDigest? GetDigest(string sessionId)
    {
        string path = _store.SessionCsvPath(sessionId);
        if (!File.Exists(path))
        {
            _cache.TryRemove(sessionId, out _);
            return null;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(path);
        if (_cache.TryGetValue(sessionId, out (DateTime WriteTime, SessionDigest Digest) cached) && cached.WriteTime == writeTime)
        {
            return cached.Digest;
        }

        string? csv = _store.ReadSessionCsv(sessionId);
        if (csv == null)
        {
            return null;
        }

        SessionDigest digest = Digest(SessionQuery.ParseEntries(csv));
        _cache[sessionId] = (writeTime, digest);
        return digest;
    }

    /// <summary>
    /// 한 세션의 로그 항목을 이벤트 규약에 따라 훑어 세션 요약으로 줄인다.
    /// 규약에 맞지 않는 항목(일반 로그·Unity 로그)은 무시한다.
    /// </summary>
    internal static SessionDigest Digest(List<LogEntry> entries)
    {
        SessionDigest d = new();
        (DateTime At, double? Money)? dayStart = null;
        Dictionary<string, DateTime> workbenchEnter = new(StringComparer.Ordinal);
        RaidState? raid = null;

        foreach (LogEntry entry in entries)
        {
            bool isShop = string.Equals(entry.Category, GameEvents.ShopCategory, StringComparison.OrdinalIgnoreCase);
            bool isRaid = !isShop && string.Equals(entry.Category, GameEvents.RaidCategory, StringComparison.OrdinalIgnoreCase);
            if (!isShop && !isRaid)
            {
                continue;
            }

            if (!CsvSessionStore.TryParseUtc(entry.TimestampUtc, out DateTime at))
            {
                continue;
            }

            string name = entry.Message.Trim();
            JsonElement data = ParseData(entry.DataJson);

            if (isShop)
            {
                raid = null;
                switch (name)
                {
                    case GameEvents.ShopStart:
                        d.ShopDays++;
                        dayStart = (at, ReadDouble(data, "money"));
                        break;

                    case GameEvents.ShopEnd:
                        double? endMoney = ReadDouble(data, "money");
                        if (dayStart is { } start)
                        {
                            d.DaySeconds.Add((at - start.At).TotalSeconds);
                            if (start.Money is double startMoney && endMoney is double gained)
                            {
                                d.MoneyGains.Add(gained - startMoney);
                            }
                        }

                        if (ReadDouble(data, "day") is double day && endMoney is double money)
                        {
                            d.MoneyAtDayEnd[(int)day] = money;
                        }

                        dayStart = null;
                        break;

                    case GameEvents.WorkbenchEnter:
                        workbenchEnter[ReadKey(data, "orderId")] = at;
                        break;

                    case GameEvents.WorkbenchExit:
                    {
                        string orderId = ReadKey(data, "orderId");
                        OrderDigest order = d.Order(orderId);
                        order.WorkbenchVisits++;
                        if (workbenchEnter.Remove(orderId, out DateTime entered))
                        {
                            order.WorkbenchSeconds += (at - entered).TotalSeconds;
                        }

                        string gun = ReadString(data, "gunCode");
                        if (gun.Length > 0)
                        {
                            order.LastBuiltGun = gun;
                        }

                        break;
                    }

                    case GameEvents.CustomerInteract:
                        d.Order(ReadKey(data, "orderId")).CustomerVisits++;
                        break;

                    case GameEvents.OrderComplete:
                    {
                        OrderDigest order = d.Order(ReadKey(data, "orderId"));
                        order.Completed = true;
                        order.Success = ReadBool(data, "success");
                        order.Died = ReadBool(data, "died");
                        string gun = ReadString(data, "gunCode");
                        order.SubmittedGun = gun.Length > 0 ? gun : order.LastBuiltGun;
                        break;
                    }

                    case GameEvents.PresetSave:
                        Increment(d.PresetGuns, ReadKeyOr(data, "gunCode"));
                        break;

                    case GameEvents.Fire:
                        d.Shots++;
                        string fireOrder = ReadString(data, "orderId");
                        if (fireOrder.Length > 0)
                        {
                            d.Order(fireOrder).Shots++;
                        }

                        break;
                }

                continue;
            }

            if (name == GameEvents.RaidStart || raid == null)
            {
                raid = new RaidState { StartedAt = at };
                d.RaidRuns++;
            }

            switch (name)
            {
                case GameEvents.RaidStart:
                    raid.StageId = ReadKeyOr(data, "stageId");
                    foreach (string gun in ReadStrings(data, "guns"))
                    {
                        Increment(d.RaidGuns, gun);
                    }

                    foreach (string module in ReadStrings(data, "modules"))
                    {
                        Increment(d.RaidModules, module);
                    }

                    break;

                case GameEvents.RaidSample:
                    AddSample(d, raid, at, data);
                    break;

                case GameEvents.BlockDestroyed:
                    string blockStage = ReadString(data, "stageId");
                    Increment(d.Blocks, blockStage.Length > 0 ? blockStage : raid.StageId);
                    break;

                case GameEvents.EnemySpawn:
                    d.Enemy(ReadKeyOr(data, "enemyId")).Spawned++;
                    break;

                case GameEvents.EnemySpotted:
                    d.Enemy(ReadKeyOr(data, "enemyId")).Spotted++;
                    break;

                case GameEvents.EnemyKilled:
                    d.Enemy(ReadKeyOr(data, "enemyId")).Killed++;
                    break;

                case GameEvents.EscapeSuccess:
                    d.Escapes.Add(new EscapeDigest
                    {
                        PlayTime = ReadDouble(data, "playTime") ?? (at - raid.StartedAt).TotalSeconds,
                        VaultReward = ReadDouble(data, "vaultReward"),
                        Kills = ReadDouble(data, "kills"),
                        Hp = ReadDouble(data, "hp"),
                    });
                    raid = null;
                    break;
            }
        }

        return d;
    }

    /// <summary>고정 주기 표본 하나를 위치 히트맵과 경과 시간 타임라인에 더한다.</summary>
    private static void AddSample(SessionDigest d, RaidState raid, DateTime at, JsonElement data)
    {
        if (ReadDouble(data, "x") is double x && ReadDouble(data, "z") is double z)
        {
            if (!d.Positions.TryGetValue(raid.StageId, out List<float>? points))
            {
                points = new List<float>();
                d.Positions[raid.StageId] = points;
            }

            points.Add((float)x);
            points.Add((float)z);
        }

        int bin = (int)((at - raid.StartedAt).TotalSeconds / TimelineBinSeconds);
        if (bin < 0 || bin >= MAX_TIMELINE_BINS)
        {
            return;
        }

        if (!d.Timeline.TryGetValue(bin, out TimelineAccumulator? acc))
        {
            acc = new TimelineAccumulator();
            d.Timeline[bin] = acc;
        }

        acc.Add(0, ReadDouble(data, "hp"));
        acc.Add(1, ReadDouble(data, "vaults"));
        acc.Add(2, ReadDouble(data, "tanks"));
        acc.Add(3, ReadDouble(data, "helis"));
    }

    /// <summary>스테이지별 위치 범위를 전 버전 공통으로 구한다. 버전 간 히트맵을 같은 좌표로 비교하기 위함이다.</summary>
    private static Dictionary<string, HeatmapBounds> ComputeHeatmapBounds(IEnumerable<SessionDigest> digests)
    {
        Dictionary<string, HeatmapBounds> bounds = new(StringComparer.Ordinal);
        foreach (SessionDigest digest in digests)
        {
            foreach (KeyValuePair<string, List<float>> pair in digest.Positions)
            {
                if (!bounds.TryGetValue(pair.Key, out HeatmapBounds? b))
                {
                    b = new HeatmapBounds();
                    bounds[pair.Key] = b;
                }

                List<float> points = pair.Value;
                for (int i = 0; i + 1 < points.Count; i += 2)
                {
                    b.Include(points[i], points[i + 1]);
                }
            }
        }

        return bounds;
    }

    /// <summary>한 버전에 속한 세션 요약을 합산해 버전 집계를 만든다.</summary>
    private static VersionAnalytics Aggregate(string version, List<SessionDigest> digests, Dictionary<string, HeatmapBounds> bounds)
    {
        VersionAnalytics v = new() { Version = version, Sessions = digests.Count };
        int[] shotHistogram = new int[ShotBuckets];
        Dictionary<string, int> submittedGuns = new(StringComparer.Ordinal);
        Dictionary<string, int> presetGuns = new(StringComparer.Ordinal);
        Dictionary<string, int> raidGuns = new(StringComparer.Ordinal);
        Dictionary<string, int> raidModules = new(StringComparer.Ordinal);
        Dictionary<string, int> blocks = new(StringComparer.Ordinal);
        Dictionary<string, EnemyStat> enemies = new(StringComparer.Ordinal);
        Dictionary<int, (double Sum, int Count)> moneyByDay = new();
        TimelineAccumulator[] timeline = new TimelineAccumulator[MAX_TIMELINE_BINS];
        Dictionary<string, int[]> heat = new(StringComparer.Ordinal);
        List<double> daySeconds = new();
        List<double> moneyGains = new();
        List<double> workbenchSeconds = new();
        List<double> customerVisits = new();
        List<EscapeDigest> escapes = new();
        int completed = 0, successes = 0, deaths = 0, shots = 0;

        foreach (SessionDigest d in digests)
        {
            v.ShopDays += d.ShopDays;
            v.Shots += d.Shots;
            v.RaidRuns += d.RaidRuns;
            daySeconds.AddRange(d.DaySeconds);
            moneyGains.AddRange(d.MoneyGains);
            escapes.AddRange(d.Escapes);
            MergeCounts(presetGuns, d.PresetGuns);
            MergeCounts(raidGuns, d.RaidGuns);
            MergeCounts(raidModules, d.RaidModules);
            MergeCounts(blocks, d.Blocks);

            foreach (KeyValuePair<int, double> pair in d.MoneyAtDayEnd)
            {
                moneyByDay.TryGetValue(pair.Key, out (double Sum, int Count) cur);
                moneyByDay[pair.Key] = (cur.Sum + pair.Value, cur.Count + 1);
            }

            foreach (OrderDigest order in d.Orders.Values)
            {
                v.Orders++;
                shotHistogram[Math.Min(order.Shots, ShotBuckets - 1)]++;
                shots += order.Shots;
                if (order.WorkbenchVisits > 0)
                {
                    workbenchSeconds.Add(order.WorkbenchSeconds);
                }

                customerVisits.Add(order.CustomerVisits);
                if (!order.Completed)
                {
                    continue;
                }

                completed++;
                if (order.Success)
                {
                    successes++;
                }

                if (order.Died)
                {
                    deaths++;
                }

                Increment(submittedGuns, string.IsNullOrEmpty(order.SubmittedGun) ? UNKNOWN : order.SubmittedGun);
            }

            foreach (KeyValuePair<string, EnemyDigest> pair in d.Enemies)
            {
                if (!enemies.TryGetValue(pair.Key, out EnemyStat? stat))
                {
                    stat = new EnemyStat { EnemyId = pair.Key };
                    enemies[pair.Key] = stat;
                }

                stat.Spawned += pair.Value.Spawned;
                stat.Spotted += pair.Value.Spotted;
                stat.Killed += pair.Value.Killed;
            }

            foreach (KeyValuePair<int, TimelineAccumulator> pair in d.Timeline)
            {
                (timeline[pair.Key] ??= new TimelineAccumulator()).Merge(pair.Value);
            }

            foreach (KeyValuePair<string, List<float>> pair in d.Positions)
            {
                HeatmapBounds b = bounds[pair.Key];
                if (!heat.TryGetValue(pair.Key, out int[]? cells))
                {
                    cells = new int[HEATMAP_GRID * HEATMAP_GRID];
                    heat[pair.Key] = cells;
                }

                List<float> points = pair.Value;
                for (int i = 0; i + 1 < points.Count; i += 2)
                {
                    cells[b.Row(points[i + 1], HEATMAP_GRID) * HEATMAP_GRID + b.Col(points[i], HEATMAP_GRID)]++;
                }
            }
        }

        v.CompletedOrders = completed;
        v.SuccessRate = completed > 0 ? (double)successes / completed : null;
        v.DeathRate = completed > 0 ? (double)deaths / completed : null;
        v.AvgShotsPerOrder = v.Orders > 0 ? (double)shots / v.Orders : null;
        v.ShotHistogram = shotHistogram;
        v.AvgDaySeconds = Average(daySeconds);
        v.AvgMoneyGain = Average(moneyGains);
        v.AvgWorkbenchSeconds = Average(workbenchSeconds);
        v.AvgCustomerVisits = Average(customerVisits);
        v.PresetSaves = presetGuns.Values.Sum();
        v.MoneyByDay = moneyByDay.OrderBy(p => p.Key)
            .Select(p => new DayMoney { Day = p.Key, AvgMoney = p.Value.Sum / p.Value.Count, Samples = p.Value.Count })
            .ToList();
        v.SubmittedGuns = Top(submittedGuns);
        v.PresetGuns = Top(presetGuns);
        v.RaidGuns = Top(raidGuns);
        v.RaidModules = Top(raidModules);
        v.Blocks = Top(blocks);
        v.Enemies = enemies.Values.OrderByDescending(e => e.Spawned).ThenBy(e => e.EnemyId, StringComparer.Ordinal).ToList();

        v.Escapes = escapes.Count;
        v.EscapeRate = v.RaidRuns > 0 ? (double)escapes.Count / v.RaidRuns : null;
        v.AvgPlayTime = Average(escapes.Select(e => e.PlayTime));
        v.AvgVaultReward = Average(escapes.Where(e => e.VaultReward.HasValue).Select(e => e.VaultReward!.Value));
        v.AvgKills = Average(escapes.Where(e => e.Kills.HasValue).Select(e => e.Kills!.Value));
        v.AvgEscapeHp = Average(escapes.Where(e => e.Hp.HasValue).Select(e => e.Hp!.Value));

        for (int i = 0; i < timeline.Length; i++)
        {
            if (timeline[i] is { } acc)
            {
                v.Timeline.Add(new TimelinePoint
                {
                    Sec = i * TimelineBinSeconds,
                    Hp = acc.Avg(0),
                    Vaults = acc.Avg(1),
                    Tanks = acc.Avg(2),
                    Helis = acc.Avg(3),
                    Samples = acc.Samples,
                });
            }
        }

        foreach (KeyValuePair<string, int[]> pair in heat.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            HeatmapBounds b = bounds[pair.Key];
            v.Heatmaps.Add(new Heatmap
            {
                StageId = pair.Key,
                MinX = b.MinX,
                MaxX = b.MaxX,
                MinZ = b.MinZ,
                MaxZ = b.MaxZ,
                Grid = HEATMAP_GRID,
                Cells = pair.Value,
            });
        }

        return v;
    }

    /// <summary>
    /// 버전 문자열을 점 구분 숫자 단위로 비교한다(1.10이 1.9 뒤). 숫자가 아닌 조각은 문자열로 비교한다.
    /// </summary>
    internal static int CompareVersions(string? a, string? b)
    {
        string[] left = (a ?? string.Empty).Split('.', '-', '_');
        string[] right = (b ?? string.Empty).Split('.', '-', '_');
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            string l = i < left.Length ? left[i] : string.Empty;
            string r = i < right.Length ? right[i] : string.Empty;
            int cmp = long.TryParse(l, out long ln) && long.TryParse(r, out long rn)
                ? ln.CompareTo(rn)
                : string.CompareOrdinal(l, r);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }

    private static JsonElement ParseData(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson) || dataJson[0] != '{')
        {
            return default;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(dataJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>객체에서 이름이 대소문자 무시로 일치하는 속성을 찾는다.</summary>
    private static bool TryGet(JsonElement data, string name, out JsonElement value)
    {
        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in data.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>문자열 또는 숫자 필드를 문자열로 읽는다. 없으면 빈 문자열.</summary>
    private static string ReadString(JsonElement data, string name)
    {
        if (!TryGet(data, name, out JsonElement value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };
    }

    /// <summary>주문 ID처럼 묶음 키로 쓰는 필드를 읽는다. 없으면 빈 키로 묶는다.</summary>
    private static string ReadKey(JsonElement data, string name) => ReadString(data, name);

    /// <summary>집계 표시용 키를 읽는다. 없으면 (미지정)으로 묶는다.</summary>
    private static string ReadKeyOr(JsonElement data, string name)
    {
        string text = ReadString(data, name);
        return text.Length > 0 ? text : UNKNOWN;
    }

    private static double? ReadDouble(JsonElement data, string name)
    {
        if (!TryGet(data, name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>true/false, 0/1, "true" 형태를 모두 참·거짓으로 읽는다.</summary>
    private static bool ReadBool(JsonElement data, string name)
    {
        if (!TryGet(data, name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetDouble(out double n) && n != 0,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase) || value.GetString() == "1",
            _ => false,
        };
    }

    /// <summary>문자열 배열 필드를 읽는다. 쉼표 구분 문자열도 배열로 받아들인다.</summary>
    private static IEnumerable<string> ReadStrings(JsonElement data, string name)
    {
        if (!TryGet(data, name, out JsonElement value))
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() ?? string.Empty : item.GetRawText())
                .Where(text => text.Length > 0)
                .ToList();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return (value.GetString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return Array.Empty<string>();
    }

    private static void Increment(Dictionary<string, int> map, string key)
    {
        map.TryGetValue(key, out int count);
        map[key] = count + 1;
    }

    private static void MergeCounts(Dictionary<string, int> target, Dictionary<string, int> source)
    {
        foreach (KeyValuePair<string, int> pair in source)
        {
            target.TryGetValue(pair.Key, out int count);
            target[pair.Key] = count + pair.Value;
        }
    }

    private static List<KeyCount> Top(Dictionary<string, int> map)
    {
        return map.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(TOP_LIST_SIZE)
            .Select(p => new KeyCount { Key = p.Key, Count = p.Value })
            .Concat(map.Count > TOP_LIST_SIZE
                ? new[] { new KeyCount { Key = "기타", Count = map.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Skip(TOP_LIST_SIZE).Sum(p => p.Value) } }
                : Array.Empty<KeyCount>())
            .ToList();
    }

    private static double? Average(IEnumerable<double> values)
    {
        double sum = 0;
        int count = 0;
        foreach (double value in values)
        {
            sum += value;
            count++;
        }

        return count > 0 ? sum / count : null;
    }

    /// <summary>진행 중인 습격 하나의 상태.</summary>
    private sealed class RaidState
    {
        public DateTime StartedAt;
        public string StageId = UNKNOWN;
    }

    /// <summary>스테이지 하나의 위치 범위.</summary>
    private sealed class HeatmapBounds
    {
        public double MinX = double.MaxValue, MaxX = double.MinValue, MinZ = double.MaxValue, MaxZ = double.MinValue;

        public void Include(double x, double z)
        {
            MinX = Math.Min(MinX, x);
            MaxX = Math.Max(MaxX, x);
            MinZ = Math.Min(MinZ, z);
            MaxZ = Math.Max(MaxZ, z);
        }

        public int Col(double x, int grid) => Cell(x, MinX, MaxX, grid);

        public int Row(double z, int grid) => Cell(z, MinZ, MaxZ, grid);

        private static int Cell(double value, double min, double max, int grid)
        {
            double span = max - min;
            return span <= 0 ? 0 : Math.Clamp((int)((value - min) / span * grid), 0, grid - 1);
        }
    }
}

/// <summary>한 세션을 이벤트 규약으로 줄인 요약. 버전 집계의 입력이며 CSV 수정 시각 기준으로 캐시된다.</summary>
internal sealed class SessionDigest
{
    public int ShopDays;
    public int Shots;
    public readonly List<double> DaySeconds = new();
    public readonly List<double> MoneyGains = new();
    public readonly Dictionary<int, double> MoneyAtDayEnd = new();
    public readonly Dictionary<string, OrderDigest> Orders = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> PresetGuns = new(StringComparer.Ordinal);

    public int RaidRuns;
    public readonly List<EscapeDigest> Escapes = new();
    public readonly Dictionary<string, int> RaidGuns = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> RaidModules = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> Blocks = new(StringComparer.Ordinal);
    public readonly Dictionary<string, EnemyDigest> Enemies = new(StringComparer.Ordinal);
    public readonly Dictionary<string, List<float>> Positions = new(StringComparer.Ordinal);
    public readonly Dictionary<int, TimelineAccumulator> Timeline = new();

    public OrderDigest Order(string orderId)
    {
        if (!Orders.TryGetValue(orderId, out OrderDigest? order))
        {
            order = new OrderDigest();
            Orders[orderId] = order;
        }

        return order;
    }

    public EnemyDigest Enemy(string enemyId)
    {
        if (!Enemies.TryGetValue(enemyId, out EnemyDigest? enemy))
        {
            enemy = new EnemyDigest();
            Enemies[enemyId] = enemy;
        }

        return enemy;
    }
}

/// <summary>주문 하나의 진행 요약.</summary>
internal sealed class OrderDigest
{
    public int Shots;
    public int WorkbenchVisits;
    public double WorkbenchSeconds;
    public int CustomerVisits;
    public bool Completed;
    public bool Success;
    public bool Died;
    public string LastBuiltGun = string.Empty;
    public string SubmittedGun = string.Empty;
}

/// <summary>탈출 성공 한 건.</summary>
internal sealed class EscapeDigest
{
    public double PlayTime;
    public double? VaultReward;
    public double? Kills;
    public double? Hp;
}

/// <summary>적 종류 하나의 생성·발견·처치 횟수.</summary>
internal sealed class EnemyDigest
{
    public int Spawned;
    public int Spotted;
    public int Killed;
}

/// <summary>타임라인 구간 하나의 체력·금고·탱크·헬기 합계와 표본 수.</summary>
internal sealed class TimelineAccumulator
{
    private readonly double[] _sums = new double[4];
    private readonly int[] _counts = new int[4];

    public int Samples { get; private set; }

    public void Add(int slot, double? value)
    {
        if (slot == 0)
        {
            Samples++;
        }

        if (value is double v)
        {
            _sums[slot] += v;
            _counts[slot]++;
        }
    }

    public void Merge(TimelineAccumulator other)
    {
        Samples += other.Samples;
        for (int i = 0; i < _sums.Length; i++)
        {
            _sums[i] += other._sums[i];
            _counts[i] += other._counts[i];
        }
    }

    public double? Avg(int slot) => _counts[slot] > 0 ? _sums[slot] / _counts[slot] : null;
}
