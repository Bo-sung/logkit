using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace LogServer;

/// <summary>
/// 게임이 실제로 남기는 이벤트 이름. category와 message(한글 이벤트 이름)로 식별하고 data_json 필드를 읽는다.
/// 집계 규칙은 ARMORY 대시보드(armory-dashboard/README.md)와 같다.
/// Unity 콘솔 판매 출력([Customer] 총 판매 …)은 Shop 판매와 중복이므로 Unity 카테고리 전체를 무시한다.
/// </summary>
internal static class GameEvents
{
    public const string ShopCategory = "Shop";
    public const string WorkshopCategory = "Workshop";
    public const string RaidCategory = "NightRaid";

    public const string Sale = "총 판매";                    // price, satisfied, tagCount
    public const string WorkbenchEnter = "작업대 진입";
    public const string WorkbenchExit = "작업대 퇴장";

    public const string RaidStart = "습격 시작";              // scene
    public const string PlayerState = "플레이어 상태";        // time, position{x,y,z}, cameraRotation, health, remainingModuleCrates, remainingMaterialCrates, tanks, helicopters
    public const string BlockDestroyed = "블록 파괴";         // stageId, time, blockId
    public const string EnemySpawn = "적 생성";               // time, enemyId
    public const string EnemySpotted = "적 발견";             // time, enemyId
    public const string EnemyKilled = "적 처치";              // time, enemyId

    // 종료 이벤트: scene, survived, time, duration, moduleCrateRewards[], moduleCrateCredits, materialCrateCredits, enemyKills, health
    public static readonly string[] RaidEnds = { "습격 사망", "탈출 성공", "습격 성공" };
}

/// <summary>
/// 버전별 누적 플레이 통계 집계기. 범위 안의 모든 세션 CSV를 실제 이벤트 규약으로 세션 요약(digest)으로 줄이고,
/// 같은 앱 버전(appVersion)의 요약을 합산해 대시보드 차트용 응답을 만든다.
/// 세션 요약은 CSV 수정 시각 기준으로 캐시해 같은 세션을 매 요청마다 다시 파싱하지 않는다.
/// </summary>
public sealed class GameAnalytics
{
    /// <summary>습격 상태 타임라인 구간 길이(게임 시간 초).</summary>
    public const int TimelineBinSeconds = 5;

    private const int MAX_TIMELINE_BINS = 720;
    private const int HEATMAP_GRID = 64;
    private const int TOP_LIST_SIZE = 15;
    private const string UNKNOWN = "(미기록)";

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
    /// 범위 안의 세션을 버전별로 누적 집계한다.
    /// month(yyyy-MM, 비면 전체 월)와 excludeEditor를 사용하며, 버전 오름차순 집계 응답을 반환한다.
    /// </summary>
    public AnalyticsResponse Build(string? month, bool excludeEditor)
    {
        List<string> months = string.IsNullOrEmpty(month)
            ? _summary.ListMonths().Select(info => info.Month).ToList()
            : new List<string> { month };

        Dictionary<string, List<(SessionRow Row, SessionDigest Digest)>> byVersion = new(StringComparer.Ordinal);
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
                if (!byVersion.TryGetValue(version, out List<(SessionRow, SessionDigest)>? list))
                {
                    list = new List<(SessionRow, SessionDigest)>();
                    byVersion[version] = list;
                }

                list.Add((row, digest));
            }
        }

        Dictionary<string, HeatmapBounds> bounds = ComputeHeatmapBounds(byVersion.Values.SelectMany(list => list.Select(item => item.Digest)));
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
    /// 한 세션의 로그 항목을 실제 이벤트 규약으로 훑어 세션 요약으로 줄인다.
    /// 판매는 Shop / 총 판매만, 작업대 체류는 같은 씬의 시간순 진입→퇴장 쌍으로,
    /// 습격은 습격 시작부터 종료 이벤트(또는 survived 필드)까지를 한 구간으로 본다. 규약 밖 항목은 무시한다.
    /// </summary>
    internal static SessionDigest Digest(List<LogEntry> entries)
    {
        List<(DateTime At, int Index, LogEntry Entry)> ordered = new(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            if (CsvSessionStore.TryParseUtc(entries[i].TimestampUtc, out DateTime at))
            {
                ordered.Add((at, i, entries[i]));
            }
        }

        ordered.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Index.CompareTo(b.Index));

        SessionDigest d = new();
        (DateTime At, string Scene)? openWorkbench = null;
        RaidDigest? raid = null;

        foreach ((DateTime at, _, LogEntry entry) in ordered)
        {
            string name = entry.Message.Trim();
            if (Is(entry.Category, GameEvents.ShopCategory))
            {
                if (name != GameEvents.Sale)
                {
                    continue;
                }

                JsonElement data = ParseData(entry.DataJson);
                d.Sales++;
                if (ReadDouble(data, "price") is double price)
                {
                    d.Prices.Add(price);
                }

                if (TryGet(data, "satisfied", out JsonElement satisfied) && satisfied.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    d.SalesWithSatisfied++;
                    if (satisfied.ValueKind == JsonValueKind.True)
                    {
                        d.SatisfiedSales++;
                    }
                }

                if (ReadDouble(data, "tagCount") is double tags)
                {
                    d.TagCounts.Add((int)tags);
                }
            }
            else if (Is(entry.Category, GameEvents.WorkshopCategory))
            {
                if (name == GameEvents.WorkbenchEnter)
                {
                    if (openWorkbench != null)
                    {
                        d.UnmatchedWorkbench++;
                    }

                    openWorkbench = (at, entry.Scene);
                }
                else if (name == GameEvents.WorkbenchExit)
                {
                    if (openWorkbench is { } open && open.Scene == entry.Scene && at >= open.At)
                    {
                        d.WorkbenchSeconds.Add((at - open.At).TotalSeconds);
                        openWorkbench = null;
                    }
                    else
                    {
                        d.UnmatchedWorkbench++;
                    }
                }
            }
            else if (Is(entry.Category, GameEvents.RaidCategory))
            {
                JsonElement data = ParseData(entry.DataJson);
                if (name == GameEvents.RaidStart)
                {
                    raid = new RaidDigest { HasStart = true, Scene = ReadString(data, "scene") };
                    d.Raids.Add(raid);
                }
                else if (raid == null)
                {
                    raid = new RaidDigest();
                    d.Raids.Add(raid);
                }

                if (raid.Scene.Length == 0 && name != GameEvents.RaidStart)
                {
                    // 습격 시작은 씬 전환 전(Main)에 기록되므로 시작에 씬이 없으면 이후 첫 행의 씬을 습격 씬으로 쓴다.
                    raid.Scene = entry.Scene;
                }

                ApplyRaidEvent(raid, name, data);
                if (GameEvents.RaidEnds.Contains(name) || TryGet(data, "survived", out JsonElement s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    raid.Ended = true;
                    raid.Survived = TryGet(data, "survived", out JsonElement survived) ? survived.ValueKind == JsonValueKind.True : null;
                    raid.Duration = ReadDouble(data, "duration");
                    raid.EnemyKills = ReadDouble(data, "enemyKills");
                    raid.EndHealth = ReadDouble(data, "health");
                    raid.ModuleCrateCredits = ReadDouble(data, "moduleCrateCredits");
                    raid.MaterialCrateCredits = ReadDouble(data, "materialCrateCredits");
                    raid.ModuleRewards.AddRange(ReadStrings(data, "moduleCrateRewards"));
                    raid = null;
                }
            }
        }

        if (openWorkbench != null)
        {
            d.UnmatchedWorkbench++;
        }

        foreach (RaidDigest r in d.Raids)
        {
            if (r.Scene.Length == 0)
            {
                r.Scene = UNKNOWN;
            }
        }

        return d;
    }

    /// <summary>습격 구간 안의 상태·블록·적 이벤트 하나를 구간 요약에 더한다.</summary>
    private static void ApplyRaidEvent(RaidDigest raid, string name, JsonElement data)
    {
        switch (name)
        {
            case GameEvents.PlayerState:
                if (ReadDouble(data, "time") is not double time
                    || !TryGet(data, "position", out JsonElement position)
                    || ReadDouble(position, "x") is not double x
                    || ReadDouble(position, "z") is not double z)
                {
                    return;
                }

                raid.States++;
                raid.Points.Add((float)x);
                raid.Points.Add((float)z);
                int bin = (int)(time / TimelineBinSeconds);
                if (bin >= 0 && bin < MAX_TIMELINE_BINS)
                {
                    if (!raid.Timeline.TryGetValue(bin, out TimelineAccumulator? acc))
                    {
                        acc = new TimelineAccumulator();
                        raid.Timeline[bin] = acc;
                    }

                    acc.AddSample();
                    for (int slot = 0; slot < TimelineAccumulator.Fields.Length; slot++)
                    {
                        acc.Add(slot, ReadDouble(data, TimelineAccumulator.Fields[slot]));
                    }
                }

                break;

            case GameEvents.BlockDestroyed:
                raid.Blocks++;
                break;

            case GameEvents.EnemySpawn:
                raid.EnemiesSpawned++;
                break;

            case GameEvents.EnemySpotted:
                raid.EnemiesSpotted++;
                break;

            case GameEvents.EnemyKilled:
                raid.EnemiesKilled++;
                break;
        }
    }

    /// <summary>스테이지(습격 씬)별 위치 범위를 전 버전 공통으로 구한다. 버전 간 히트맵을 같은 좌표로 비교하기 위함이다.</summary>
    private static Dictionary<string, HeatmapBounds> ComputeHeatmapBounds(IEnumerable<SessionDigest> digests)
    {
        Dictionary<string, HeatmapBounds> bounds = new(StringComparer.Ordinal);
        foreach (RaidDigest raid in digests.SelectMany(d => d.Raids))
        {
            if (raid.Points.Count == 0)
            {
                continue;
            }

            if (!bounds.TryGetValue(raid.Scene, out HeatmapBounds? b))
            {
                b = new HeatmapBounds();
                bounds[raid.Scene] = b;
            }

            for (int i = 0; i + 1 < raid.Points.Count; i += 2)
            {
                b.Include(raid.Points[i], raid.Points[i + 1]);
            }
        }

        foreach (HeatmapBounds b in bounds.Values)
        {
            b.MakeSquare();
        }

        return bounds;
    }

    /// <summary>한 버전에 속한 세션 요약을 합산해 버전 누적 집계를 만든다.</summary>
    private static VersionAnalytics Aggregate(
        string version,
        List<(SessionRow Row, SessionDigest Digest)> sessions,
        Dictionary<string, HeatmapBounds> bounds)
    {
        VersionAnalytics v = new() { Version = version, Sessions = sessions.Count };
        List<double> prices = new();
        List<int> tagCounts = new();
        List<double> workbench = new();
        Dictionary<string, StageAccumulator> stages = new(StringComparer.Ordinal);
        Dictionary<string, int> moduleRewards = new(StringComparer.Ordinal);
        TimelineAccumulator?[] timeline = new TimelineAccumulator?[MAX_TIMELINE_BINS];
        Dictionary<string, int[]> heat = new(StringComparer.Ordinal);
        List<RaidDigest> raids = new();
        int satisfied = 0, withSatisfied = 0;
        DateTime? first = null, last = null;

        foreach ((SessionRow row, SessionDigest d) in sessions)
        {
            v.PlaySeconds += row.DurationSec;
            v.CleanEnds += row.EndedCleanly ? 1 : 0;
            v.LogRows += row.Total;
            if (CsvSessionStore.TryParseUtc(row.DateUtc, out DateTime started))
            {
                first = first == null || started < first ? started : first;
                last = last == null || started > last ? started : last;
            }

            v.Sales += d.Sales;
            prices.AddRange(d.Prices);
            tagCounts.AddRange(d.TagCounts);
            satisfied += d.SatisfiedSales;
            withSatisfied += d.SalesWithSatisfied;
            workbench.AddRange(d.WorkbenchSeconds);
            v.UnmatchedWorkbench += d.UnmatchedWorkbench;
            if (d.Sales > 0)
            {
                v.SessionsWithSales++;
            }

            if (d.Raids.Count > 0)
            {
                v.SessionsWithRaids++;
            }

            raids.AddRange(d.Raids);
        }

        foreach (RaidDigest raid in raids)
        {
            if (!stages.TryGetValue(raid.Scene, out StageAccumulator? stage))
            {
                stage = new StageAccumulator();
                stages[raid.Scene] = stage;
            }

            stage.Add(raid);
            foreach (string reward in raid.ModuleRewards)
            {
                Increment(moduleRewards, reward);
            }

            foreach (KeyValuePair<int, TimelineAccumulator> pair in raid.Timeline)
            {
                (timeline[pair.Key] ??= new TimelineAccumulator()).Merge(pair.Value);
            }

            if (raid.Points.Count > 0)
            {
                HeatmapBounds b = bounds[raid.Scene];
                if (!heat.TryGetValue(raid.Scene, out int[]? cells))
                {
                    cells = new int[HEATMAP_GRID * HEATMAP_GRID];
                    heat[raid.Scene] = cells;
                }

                for (int i = 0; i + 1 < raid.Points.Count; i += 2)
                {
                    cells[b.Row(raid.Points[i + 1], HEATMAP_GRID) * HEATMAP_GRID + b.Col(raid.Points[i], HEATMAP_GRID)]++;
                }
            }
        }

        v.FirstSessionUtc = first?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
        v.LastSessionUtc = last?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
        v.Revenue = prices.Sum();
        v.PricedSales = prices.Count;
        v.Prices = prices;
        v.TagCounts = tagCounts;
        v.SatisfiedRate = withSatisfied > 0 ? (double)satisfied / withSatisfied : null;
        v.WorkbenchVisits = workbench.Count;
        v.WorkbenchSeconds = workbench;

        RaidTotals all = new();
        foreach (RaidDigest raid in raids)
        {
            all.Add(raid);
        }

        v.Raids = all.ToStat(string.Empty);
        v.Stages = stages.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value.Totals.ToStat(p.Key)).ToList();
        v.ModuleRewards = Top(moduleRewards);

        for (int i = 0; i < timeline.Length; i++)
        {
            if (timeline[i] is { } acc)
            {
                v.Timeline.Add(new TimelinePoint
                {
                    Sec = i * TimelineBinSeconds,
                    Health = acc.Avg(0),
                    ModuleCrates = acc.Avg(1),
                    MaterialCrates = acc.Avg(2),
                    Tanks = acc.Avg(3),
                    Helicopters = acc.Avg(4),
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

    private static bool Is(string category, string expected) => string.Equals(category, expected, StringComparison.Ordinal);

    private static JsonElement ParseData(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson) || dataJson.TrimStart()[0] != '{')
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

    /// <summary>객체에서 이름이 정확히 일치하는 속성을 찾는다.</summary>
    private static bool TryGet(JsonElement data, string name, out JsonElement value)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

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

    /// <summary>숫자 필드를 읽는다. 없거나 숫자가 아니면 null — 결측을 0으로 바꾸지 않는다.</summary>
    private static double? ReadDouble(JsonElement data, string name)
    {
        if (TryGet(data, name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)
            && double.IsFinite(number))
        {
            return number;
        }

        return null;
    }

    /// <summary>배열 필드의 원소를 문자열로 읽는다(숫자 ID는 원문 그대로).</summary>
    private static IEnumerable<string> ReadStrings(JsonElement data, string name)
    {
        if (!TryGet(data, name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() ?? string.Empty : item.GetRawText())
            .Where(text => text.Length > 0)
            .ToList();
    }

    private static void Increment(Dictionary<string, int> map, string key)
    {
        map.TryGetValue(key, out int count);
        map[key] = count + 1;
    }

    private static List<KeyCount> Top(Dictionary<string, int> map)
    {
        List<KeyValuePair<string, int>> sorted = map.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        List<KeyCount> top = sorted.Take(TOP_LIST_SIZE).Select(p => new KeyCount { Key = p.Key, Count = p.Value }).ToList();
        if (sorted.Count > TOP_LIST_SIZE)
        {
            top.Add(new KeyCount { Key = "기타", Count = sorted.Skip(TOP_LIST_SIZE).Sum(p => p.Value) });
        }

        return top;
    }

    /// <summary>스테이지 하나의 습격 합계.</summary>
    private sealed class StageAccumulator
    {
        public readonly RaidTotals Totals = new();

        public void Add(RaidDigest raid) => Totals.Add(raid);
    }

    /// <summary>습격 구간 여러 개의 합계. 결측값은 평균에서 빼고 표본 수를 따로 센다.</summary>
    private sealed class RaidTotals
    {
        private int _raids, _ended, _survived, _survivedKnown, _withStart, _blocks, _spawned, _spotted, _killed;
        private readonly List<double> _durations = new(), _endHealth = new(), _enemyKills = new(), _moduleCredits = new(), _materialCredits = new();

        public void Add(RaidDigest raid)
        {
            _raids++;
            _withStart += raid.HasStart ? 1 : 0;
            _blocks += raid.Blocks;
            _spawned += raid.EnemiesSpawned;
            _spotted += raid.EnemiesSpotted;
            _killed += raid.EnemiesKilled;
            if (!raid.Ended)
            {
                return;
            }

            _ended++;
            if (raid.Survived is bool survived)
            {
                _survivedKnown++;
                _survived += survived ? 1 : 0;
            }

            AddIf(_durations, raid.Duration);
            AddIf(_endHealth, raid.EndHealth);
            AddIf(_enemyKills, raid.EnemyKills);
            AddIf(_moduleCredits, raid.ModuleCrateCredits);
            AddIf(_materialCredits, raid.MaterialCrateCredits);
        }

        public RaidStat ToStat(string scene) => new()
        {
            Scene = scene,
            Raids = _raids,
            Ended = _ended,
            WithoutStart = _raids - _withStart,
            Survived = _survived,
            SurvivedRate = _survivedKnown > 0 ? (double)_survived / _survivedKnown : null,
            AvgDuration = Average(_durations),
            AvgEndHealth = Average(_endHealth),
            AvgEnemyKills = Average(_enemyKills),
            AvgModuleCrateCredits = Average(_moduleCredits),
            AvgMaterialCrateCredits = Average(_materialCredits),
            Blocks = _blocks,
            EnemiesSpawned = _spawned,
            EnemiesSpotted = _spotted,
            EnemiesKilled = _killed,
        };

        private static void AddIf(List<double> list, double? value)
        {
            if (value is double v)
            {
                list.Add(v);
            }
        }

        private static double? Average(List<double> values) => values.Count > 0 ? values.Average() : null;
    }

    /// <summary>스테이지 하나의 위치 범위. 축척을 맞추기 위해 정사각형으로 넓힌다.</summary>
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

        /// <summary>X·Z 축척을 같게 두도록 짧은 축을 가운데 기준으로 넓힌다.</summary>
        public void MakeSquare()
        {
            double span = Math.Max(1, Math.Max(MaxX - MinX, MaxZ - MinZ));
            double cx = (MinX + MaxX) / 2, cz = (MinZ + MaxZ) / 2;
            MinX = cx - span / 2;
            MaxX = cx + span / 2;
            MinZ = cz - span / 2;
            MaxZ = cz + span / 2;
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

/// <summary>한 세션을 실제 이벤트 규약으로 줄인 요약. 버전 집계의 입력이며 CSV 수정 시각 기준으로 캐시된다.</summary>
internal sealed class SessionDigest
{
    public int Sales;
    public int SatisfiedSales;
    public int SalesWithSatisfied;
    public readonly List<double> Prices = new();
    public readonly List<int> TagCounts = new();
    public readonly List<double> WorkbenchSeconds = new();
    public int UnmatchedWorkbench;
    public readonly List<RaidDigest> Raids = new();
}

/// <summary>습격 한 구간(습격 시작 ~ 종료 이벤트)의 요약.</summary>
internal sealed class RaidDigest
{
    public bool HasStart;
    public string Scene = string.Empty;
    public bool Ended;
    public bool? Survived;
    public double? Duration;
    public double? EnemyKills;
    public double? EndHealth;
    public double? ModuleCrateCredits;
    public double? MaterialCrateCredits;
    public readonly List<string> ModuleRewards = new();
    public int States;
    public int Blocks;
    public int EnemiesSpawned;
    public int EnemiesSpotted;
    public int EnemiesKilled;
    public readonly List<float> Points = new();
    public readonly Dictionary<int, TimelineAccumulator> Timeline = new();
}

/// <summary>타임라인 구간 하나의 상태 필드별 합계와 표본 수. 필드가 빠진 표본은 그 필드 평균에서 뺀다.</summary>
internal sealed class TimelineAccumulator
{
    /// <summary>플레이어 상태 data_json에서 읽는 필드. 순서가 슬롯 번호다.</summary>
    public static readonly string[] Fields = { "health", "remainingModuleCrates", "remainingMaterialCrates", "tanks", "helicopters" };

    private readonly double[] _sums = new double[Fields.Length];
    private readonly int[] _counts = new int[Fields.Length];

    public int Samples { get; private set; }

    public void AddSample() => Samples++;

    public void Add(int slot, double? value)
    {
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
