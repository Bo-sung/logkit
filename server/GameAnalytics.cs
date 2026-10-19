using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace LogServer;

/// <summary>
/// 게임이 실제로 남기는 이벤트 이름. category와 message로 식별하고 data_json 필드를 읽는다.
/// 집계 규칙은 ARMORY 대시보드(armory-dashboard/README.md)와 같다.
/// Unity 콘솔 판매 출력([Customer] 총 판매 …)은 Shop 판매와 중복이므로 Unity 카테고리 전체를 무시한다.
/// 상점 이벤트는 초기 빌드의 한글 이름(Shop / 총 판매, Workshop / 작업대 진입·퇴장)과
/// 이후 ShopLogCollector의 eventType 이름(Shop / weapon_sold 등)을 모두 받는다.
/// </summary>
internal static class GameEvents
{
    public const string ShopCategory = "Shop";
    public const string WorkshopCategory = "Workshop";
    public const string RaidCategory = "NightRaid";

    public const string LegacySale = "총 판매";              // price, satisfied, tagCount
    public const string LegacyWorkbenchEnter = "작업대 진입";
    public const string LegacyWorkbenchExit = "작업대 퇴장";

    // ShopLogCollector — 공통 필드 eventType, eventId, orderId, day
    public const string Sale = "weapon_sold";                 // price, satisfied, tagCount, weaponCode (코드는 이후 빌드부터)
    public const string WorkbenchEnter = "workbench_entered";
    public const string WorkbenchExit = "workbench_exited";   // weaponCode, weaponCodeStatus
    public const string AttackInput = "attack_input";         // weaponCode, weaponCodeStatus (발사 입력, 발사 성공 아님)
    public const string OrderCreated = "order_created";       // profileId, requestIds
    public const string OrderRestored = "order_restored";     // profileId, requestIds (저장 불러오기로 되살린 주문)
    public const string OrderCompleted = "order_completed";   // success, customerDead
    public const string ShopOpened = "shop_opened";           // balance — 영업 시작 때 잔액
    public const string ShopCloseRequested = "shop_close_requested"; // balance — 수동 영업 종료 때 잔액
    public const string SlotSpin = "slot_machine_spin_started"; // day, isNight, oddsPool, balanceBefore, price, plannedPrize, plannedSymbol — 결제 성공 때
    public const string WeaponFired = "weapon_fired";         // time, position, weaponAssemblyId (Shop: 공방 주간, NightRaid: 습격 중)
    public const string RaidWeaponConfig = "raid_weapon_config"; // time, weaponCode, weaponAssemblyId — 습격 중 처음 쓰는 구성

    public const string GameCategory = "Game";
    public const string Pause = "일시정지";                   // 일시정지 해제 또는 다음 이벤트까지를 정지 시간으로 본다
    public const string Bankrupt = "bankrupt";                // day, balance, rent — 정산 때 자리세를 못 낸 순간
    public const string RetireCompleted = "retire_completed"; // day, balanceBefore, balanceAfter, outstandingLoans, moduleCredits, endless, firstEnding
    public const string RetireFailed = "retire_failed";       // day, reason, balance
    public const string RaidFirstClear = "raid_first_clear";  // scene, day, saleBonusAdded, saleBonusTotal — 구역 첫 클리어 확정(귀환 후)
    public const string RaidSkipped = "raid_skipped";         // day, nextDay, moduleCreditsGranted, moduleCreditsAfter — 습격 없이 다음 날로
    public const string EndingChoice = "ending_choice";       // endingType(retire/end_chest), choice(lobby/continue), panelSeconds, day, firstEnding
    public const string SystemCategory = "System";           // 씬 시작·새 게임 시작·저장 게임 시작(...)에 installId, saveId

    public const string TutorialCategory = "Tutorial";
    public const string TutorialStepStarted = "step_started";     // sequence, stepId, stepIndex, time
    public const string TutorialStepCompleted = "step_completed"; // sequence, stepId, stepIndex, time, duration, debugSkipped
    public const string TutorialSkipped = "tutorial_skipped";     // sequence, reason
    public const string TutorialEnded = "tutorial_ended";         // sequence, lastStepId, lastStepIndex, reason, time

    public const string RaidStart = "습격 시작";              // scene, day, weaponCode, weaponAssemblyId, penetration, explosion (이후 빌드부터)
    public const string PlayerState = "플레이어 상태";        // time, position{x,y,z}, cameraRotation, health, remainingModuleCrates, remainingMaterialCrates, tanks, helicopters
    public const string BlockDestroyed = "블록 파괴";         // stageId, time, blockId
    public const string EnemySpawn = "적 생성";               // time, enemyId
    public const string EnemySpotted = "적 발견";             // time, enemyId
    public const string EnemyKilled = "적 처치";              // time, enemyId
    public const string PlayerHit = "player_hit";             // time, position, damage, health, enemyType

    // 종료 이벤트: scene, survived, time, duration, moduleCrateRewards[], moduleCrateCredits, materialCrateCredits, enemyKills, health
    public static readonly string[] RaidEnds = { "습격 사망", "습격 탈출 성공", "탈출 성공", "습격 성공", "습격 엔딩 종료" };
    public const string EndChestScanned = "end_chest_scanned"; // scene, day, duration, requiredGravity, requiredHoming
    public const string EndChestHit = "end_chest_hit";         // scene, day, duration, gravity, homing, scanned
    public const string EndChestOpened = "end_chest_opened";   // scene, day, duration, absorbSeconds
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
    private const int MAX_REPLAY_RAIDS = 300;

    /// <summary>습격 결과 로그 뒤 이 시간(초) 안에 같은 씬에서 온 습격 이벤트는 끝난 습격에 붙인다(늦게 기록된 블록 파괴 등).</summary>
    private const double LATE_RAID_EVENT_SECONDS = 30;

    /// <summary>리플레이 궤적 표본 간격(게임 초). 플레이어 상태는 초당 50회라 그대로 보내면 너무 크다.</summary>
    private const double REPLAY_STEP_SECONDS = 0.2;
    private const string UNKNOWN = "(미기록)";

    private readonly CsvSessionStore _store;
    private readonly SessionSummary _summary;
    private readonly ConcurrentDictionary<string, (DateTime WriteTime, SessionDigest Digest)> _cache = new(StringComparer.Ordinal);

    // 캐시에 없는 세션을 채우는 작업은 한 번에 하나만 한다(기동 직후 예열과 첫 요청이 같은 세션을 두 번 파싱하지 않도록)
    private readonly SemaphoreSlim _fillGate = new(1, 1);

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
        Dictionary<string, List<(SessionRow Row, SessionDigest Digest)>> byVersion = GroupByVersion(month, excludeEditor);
        Dictionary<string, HeatmapBounds> bounds = ComputeHeatmapBounds(
            byVersion.Values.SelectMany(list => list.Select(item => item.Digest)), raid => raid.Points);
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
    /// 범위 안 세션의 총기 코드 통계를 버전별로 누적한다.
    /// month(yyyy-MM, 비면 전체 월)와 excludeEditor를 사용하며, 버전 오름차순 총기 집계 응답을 반환한다.
    /// </summary>
    public WeaponAnalyticsResponse BuildWeapons(string? month, bool excludeEditor)
    {
        Dictionary<string, List<(SessionRow Row, SessionDigest Digest)>> byVersion = GroupByVersion(month, excludeEditor);
        Dictionary<string, HeatmapBounds> bounds = ComputeHeatmapBounds(
            byVersion.Values.SelectMany(list => list.Select(item => item.Digest)), raid => raid.ShotPoints);
        List<string> versions = byVersion.Keys.ToList();
        versions.Sort(CompareVersions);

        return new WeaponAnalyticsResponse
        {
            Month = month ?? string.Empty,
            ExcludeEditor = excludeEditor,
            Versions = versions.Select(v => AggregateWeapons(v, byVersion[v], bounds)).ToList(),
        };
    }

    /// <summary>
    /// 범위 안 세션의 가설·일반 통계 기록을 버전별로 모은다. 집계(분포·비율)는 화면이 한다.
    /// month(yyyy-MM, 비면 전체 월)와 excludeEditor를 사용하며, 버전 오름차순 기록 응답을 반환한다.
    /// </summary>
    /// <summary>
    /// 고스트 리플레이 응답. 버전·스테이지별 습격 수 목록과, 고른 버전·스테이지(없으면 최신 버전의 습격이 가장 많은 스테이지)의
    /// 습격 궤적을 돌려준다. 궤적은 세션 요약의 0.2초 간격 표본이며 최근 습격부터 MAX_REPLAY_RAIDS개까지만 담는다.
    /// </summary>
    public ReplayResponse BuildReplay(string? month, bool excludeEditor, string? version, string? stage)
    {
        Dictionary<string, List<(SessionRow Row, SessionDigest Digest)>> byVersion = GroupByVersion(month, excludeEditor);
        List<string> versions = byVersion.Keys.ToList();
        versions.Sort((a, b) => CompareVersions(b, a));
        ReplayResponse response = new() { Month = month ?? string.Empty, ExcludeEditor = excludeEditor };
        foreach (string v in versions)
        {
            List<ReplayStage> stages = byVersion[v].SelectMany(x => x.Digest.Raids)
                .Where(r => r.Track.Count > 0)
                .GroupBy(r => r.Scene.Length > 0 ? r.Scene : UNKNOWN)
                .Select(g => new ReplayStage { Scene = g.Key, Raids = g.Count() })
                .OrderByDescending(s => s.Raids).ThenBy(s => s.Scene, StringComparer.Ordinal)
                .ToList();
            if (stages.Count > 0)
            {
                response.Versions.Add(new ReplayVersion { Version = v, Stages = stages });
            }
        }

        ReplayVersion? chosen = response.Versions.Find(x => x.Version == version) ?? response.Versions.FirstOrDefault();
        if (chosen == null)
        {
            return response;
        }

        string scene = chosen.Stages.Exists(s => s.Scene == stage) ? stage! : chosen.Stages[0].Scene;
        response.Version = chosen.Version;
        response.Stage = scene;
        List<(SessionRow Row, SessionDigest Digest)> sessions = byVersion[chosen.Version];
        sessions.Sort((a, b) => string.CompareOrdinal(b.Row.DateUtc, a.Row.DateUtc));
        foreach ((SessionRow row, SessionDigest digest) in sessions)
        {
            int index = 0;
            foreach (RaidDigest raid in digest.Raids)
            {
                index++;
                if (raid.Track.Count == 0 || (raid.Scene.Length > 0 ? raid.Scene : UNKNOWN) != scene)
                {
                    continue;
                }

                if (response.Raids.Count >= MAX_REPLAY_RAIDS)
                {
                    response.Truncated = true;
                    break;
                }

                response.Raids.Add(new ReplayRaid
                {
                    SessionId = row.SessionId,
                    DateUtc = row.DateUtc,
                    Index = index,
                    Day = raid.Day,
                    Survived = raid.Survived,
                    Exit = raid.Exit,
                    Duration = raid.Duration,
                    Track = raid.Track,
                    Shots = raid.ShotTrack,
                    Hits = raid.HitTrack,
                    Kills = raid.KillTimes,
                });
            }
        }

        return response;
    }

    public InsightsResponse BuildInsights(string? month, bool excludeEditor)
    {
        Dictionary<string, List<(SessionRow Row, SessionDigest Digest)>> byVersion = GroupByVersion(month, excludeEditor);
        List<string> versions = byVersion.Keys.ToList();
        versions.Sort(CompareVersions);

        InsightsResponse response = new()
        {
            Month = month ?? string.Empty,
            ExcludeEditor = excludeEditor,
            Stages = RaidStages.Select(p => new InsightStage { Scene = p.Scene, Name = p.Name, VaultPenetration = p.Vault }).ToList(),
        };
        foreach (string version in versions)
        {
            List<(SessionRow Row, SessionDigest Digest)> sessions = byVersion[version];
            sessions.Sort((a, b) => string.CompareOrdinal(a.Row.DateUtc, b.Row.DateUtc));
            response.Versions.Add(InsightPlayers.Combine(version, sessions.Select(x => (x.Row, x.Digest.Insights)).ToList()));
        }

        return response;
    }

    /// <summary>
    /// 습격 스테이지 표. 게임 Assets/Resources/RaidMapData.json의 regions 순서(입장 비용 오름차순)와 표시 이름이다.
    /// Vault는 게임 담당이 알려 준 현재 씬의 금고 관통 내성(은행 금고 벽 3, 나머지 1)이며 기획의 대표 값은 아직 정해지지 않았다.
    /// </summary>
    private static readonly (string Scene, string Name, double Vault)[] RaidStages =
    {
        ("Tutorial_WoodenHouse", "튜토리얼 목조주택", 1),
        ("NightRaid_WoodenHouse", "목조주택", 1),
        ("NightRaid_BankGrand", "은행", 3),
        ("NightRaid_JungleCampus", "정글 캠퍼스", 1),
        ("NightRaid_Millitary_base", "군사기지", 1),
        ("NightRaid_JungleCampus_improve", "정글 캠퍼스 (개선)", 1),
    };

    /// <summary>범위 안 세션을 요약(digest)과 함께 앱 버전별로 묶는다. CSV가 없는 세션은 뺀다.</summary>
    private Dictionary<string, List<(SessionRow Row, SessionDigest Digest)>> GroupByVersion(string? month, bool excludeEditor)
    {
        List<string> months = string.IsNullOrEmpty(month)
            ? _summary.ListMonths().Select(info => info.Month).ToList()
            : new List<string> { month };

        List<SessionRow> rows = months.SelectMany(m => _summary.GetMonthSessions(m))
            .Where(row => !excludeEditor || !row.IsEditor)
            .ToList();
        FillCache(rows);

        Dictionary<string, List<(SessionRow Row, SessionDigest Digest)>> byVersion = new(StringComparer.Ordinal);
        foreach (SessionRow row in rows)
        {
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

        return byVersion;
    }

    /// <summary>
    /// 전체 세션의 요약을 미리 만들어 둔다. 기동 직후 첫 통계 요청이 모든 CSV를 파싱하느라 오래 걸리지 않게 한다.
    /// </summary>
    public void Warm()
    {
        FillCache(_summary.ListMonths().SelectMany(info => _summary.GetMonthSessions(info.Month)).ToList());
    }

    /// <summary>캐시에 없거나 CSV가 바뀐 세션의 요약을 병렬로 만든다. 세션 하나의 요약은 다른 세션과 독립이다.</summary>
    private void FillCache(List<SessionRow> rows)
    {
        List<string> stale = rows.Select(row => row.SessionId).Where(IsStale).Distinct(StringComparer.Ordinal).ToList();
        if (stale.Count == 0)
        {
            return;
        }

        _fillGate.Wait();
        try
        {
            ParallelOptions parallel = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
            // 큰 세션부터 시작해야 마지막에 큰 파일 하나만 남아 한 스레드로 도는 일이 줄어든다
            Parallel.ForEach(stale.Where(IsStale).OrderByDescending(SessionSize), parallel, id => GetDigest(id));
        }
        finally
        {
            _fillGate.Release();
        }
    }

    private bool IsStale(string sessionId)
    {
        string path = _store.SessionCsvPath(sessionId);
        return File.Exists(path)
            && !(_cache.TryGetValue(sessionId, out (DateTime WriteTime, SessionDigest Digest) cached) && cached.WriteTime == File.GetLastWriteTimeUtc(path));
    }

    private long SessionSize(string sessionId)
    {
        FileInfo file = new(_store.SessionCsvPath(sessionId));
        return file.Exists ? file.Length : 0;
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

        List<LogEntry>? entries = _store.ReadSessionEntries(sessionId);
        if (entries == null)
        {
            return null;
        }

        SessionDigest digest = Digest(entries);
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
        WeaponTracker weapons = new(d.Weapons);
        InsightTracker insights = new(d.Insights);
        (DateTime At, string Scene)? openWorkbench = null;
        RaidDigest? raid = null;
        (RaidDigest Raid, DateTime At)? ended = null;
        int? shopDay = null;

        void EnterWorkbench(DateTime at, string scene)
        {
            if (openWorkbench != null)
            {
                d.UnmatchedWorkbench++;
            }

            openWorkbench = (at, scene);
        }

        void ExitWorkbench(DateTime at, string scene)
        {
            if (openWorkbench is { } open && open.Scene == scene && at >= open.At)
            {
                d.WorkbenchSeconds.Add((at - open.At).TotalSeconds);
                openWorkbench = null;
            }
            else
            {
                d.UnmatchedWorkbench++;
            }
        }

        foreach ((DateTime at, _, LogEntry entry) in ordered)
        {
            string name = entry.Message.Trim();
            insights.Observe(at, entry.Category, name);
            if (Is(entry.Category, GameEvents.ShopCategory))
            {
                JsonElement shop = ParseData(entry.DataJson);
                insights.Shop(at, name, shop);
                if (ReadDouble(shop, "day") is double day && day >= 0 && day < 1000)
                {
                    shopDay = (int)day;
                }

                switch (name)
                {
                    case GameEvents.Sale:
                    case GameEvents.LegacySale:
                        AddSale(d, shop);
                        weapons.Sale(shop);
                        break;

                    case GameEvents.WorkbenchEnter:
                        EnterWorkbench(at, entry.Scene);
                        break;

                    case GameEvents.WorkbenchExit:
                        ExitWorkbench(at, entry.Scene);
                        weapons.WorkbenchExit(shop);
                        break;

                    case GameEvents.AttackInput:
                        weapons.AttackInput(shop);
                        break;

                    case GameEvents.WeaponFired:
                        weapons.ShopShot(shop);
                        break;

                    case GameEvents.OrderCompleted:
                        JsonElement order = shop;
                        d.OrdersCompleted++;
                        d.OrdersSucceeded += TryGet(order, "success", out JsonElement success) && success.ValueKind == JsonValueKind.True ? 1 : 0;
                        d.CustomerDeaths += TryGet(order, "customerDead", out JsonElement dead) && dead.ValueKind == JsonValueKind.True ? 1 : 0;
                        break;
                }
            }
            else if (Is(entry.Category, GameEvents.WorkshopCategory))
            {
                if (name == GameEvents.LegacyWorkbenchEnter)
                {
                    EnterWorkbench(at, entry.Scene);
                }
                else if (name == GameEvents.LegacyWorkbenchExit)
                {
                    ExitWorkbench(at, entry.Scene);
                }
            }
            else if (Is(entry.Category, GameEvents.SystemCategory) || Is(entry.Category, GameEvents.GameCategory)
                || Is(entry.Category, GameEvents.TutorialCategory))
            {
                insights.Other(entry.Category, name, ParseData(entry.DataJson));
            }
            else if (Is(entry.Category, GameEvents.RaidCategory))
            {
                JsonElement data = ParseData(entry.DataJson);
                if (name == GameEvents.RaidStart)
                {
                    // 습격 일차: 습격 시작의 day(출발한 공방 일차), 없으면 직전 상점 이벤트의 일차
                    raid = new RaidDigest
                    {
                        HasStart = true,
                        Scene = ReadString(data, "scene"),
                        Day = ReadDouble(data, "day") is double startDay && startDay >= 0 ? (int)startDay : shopDay,
                    };
                    d.Raids.Add(raid);
                    ended = null;
                    insights.RaidStart(at, raid.Scene, data);
                }
                else
                {
                    if (raid == null && ended is { } last && last.Raid.Scene == entry.Scene
                        && (at - last.At).TotalSeconds <= LATE_RAID_EVENT_SECONDS)
                    {
                        // 결과 로그 뒤에 늦게 온 같은 씬의 이벤트는 새 습격이 아니라 방금 끝난 습격의 것이다
                        // (게임이 블록 파괴 로그를 모았다가 결과 기록·씬 종료 때 한꺼번에 남긴다)
                        ApplyRaidEvent(last.Raid, name, data);
                        weapons.RaidEvent(last.Raid, name, data);
                        continue;
                    }

                    insights.RaidEvent(entry.Scene, name, data);
                    if (raid == null)
                    {
                        raid = new RaidDigest { Day = shopDay };
                        d.Raids.Add(raid);
                    }
                }

                if (raid.Scene.Length == 0 && name != GameEvents.RaidStart)
                {
                    // 습격 시작은 씬 전환 전(Main)에 기록되므로 시작에 씬이 없으면 이후 첫 행의 씬을 습격 씬으로 쓴다.
                    raid.Scene = entry.Scene;
                }

                ApplyRaidEvent(raid, name, data);
                weapons.RaidEvent(raid, name, data);
                if (GameEvents.RaidEnds.Contains(name) || TryGet(data, "survived", out JsonElement s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    raid.Ended = true;
                    raid.Survived = TryGet(data, "survived", out JsonElement survived) ? survived.ValueKind == JsonValueKind.True : null;
                    raid.Duration = ReadDouble(data, "duration");
                    raid.Exit = ReadString(data, "exitReason");
                    raid.EnemyKills = ReadDouble(data, "enemyKills");
                    raid.EndHealth = ReadDouble(data, "health");
                    raid.ModuleCrateCredits = ReadDouble(data, "moduleCrateCredits");
                    raid.MaterialCrateCredits = ReadDouble(data, "materialCrateCredits");
                    raid.ModuleRewards.AddRange(ReadStrings(data, "moduleCrateRewards"));
                    insights.RaidEnd(at, data);
                    ended = (raid, at);
                    raid = null;
                }
            }
        }

        if (openWorkbench != null)
        {
            d.UnmatchedWorkbench++;
        }

        insights.Finish(d.Weapons);
        foreach (RaidDigest r in d.Raids)
        {
            if (r.Scene.Length == 0)
            {
                r.Scene = UNKNOWN;
            }
        }

        return d;
    }

    /// <summary>판매 이벤트 하나의 price·satisfied·tagCount를 세션 요약에 더한다. 결측 필드는 해당 목록에서 뺀다.</summary>
    private static void AddSale(SessionDigest d, JsonElement data)
    {
        d.Sales++;
        if (ReadDouble(data, "price") is double price)
        {
            d.Prices.Add(price);
        }

        if (ReadBool(data, "satisfied") is bool satisfied)
        {
            d.SalesWithSatisfied++;
            d.SatisfiedSales += satisfied ? 1 : 0;
        }

        if (ReadDouble(data, "tagCount") is double tags)
        {
            d.TagCounts.Add((int)tags);
        }
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
                if (time - raid.LastTrackTime >= REPLAY_STEP_SECONDS)
                {
                    // 리플레이 궤적: 시각, x, z, 시선 방향(cameraRotation.y, 도), 체력
                    raid.LastTrackTime = time;
                    double yaw = TryGet(data, "cameraRotation", out JsonElement rotation) ? ReadDouble(rotation, "y") ?? 0 : 0;
                    raid.Track.AddRange(new[] { Round(time), Round(x), Round(z), (float)Math.Round(yaw), Round(ReadDouble(data, "health") ?? 0) });
                }
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
                if (ReadDouble(data, "time") is double killedAt)
                {
                    raid.KillTimes.Add(Round(killedAt));
                }

                break;

            case GameEvents.WeaponFired:
            case GameEvents.PlayerHit:
                if (ReadDouble(data, "time") is double eventAt && TryGet(data, "position", out JsonElement at)
                    && ReadDouble(at, "x") is double ex && ReadDouble(at, "z") is double ez)
                {
                    (name == GameEvents.WeaponFired ? raid.ShotTrack : raid.HitTrack).AddRange(new[] { Round(eventAt), Round(ex), Round(ez) });
                }

                break;
        }
    }

    /// <summary>구간 누적(5초 단위)을 표본이 있는 구간의 평균 상태 목록으로 바꾼다.</summary>
    private static List<TimelinePoint> TimelinePoints(TimelineAccumulator?[] timeline)
    {
        List<TimelinePoint> points = new();
        for (int i = 0; i < timeline.Length; i++)
        {
            if (timeline[i] is { } acc)
            {
                points.Add(new TimelinePoint
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

        return points;
    }

    private static float Round(double value) => (float)Math.Round(value, 2);

    /// <summary>
    /// 스테이지(습격 씬)별 위치 범위를 전 버전 공통으로 구한다. 버전 간 히트맵을 같은 좌표로 비교하기 위함이다.
    /// pointsOf는 습격 구간에서 x, z를 번갈아 담은 좌표 목록(플레이어 상태 위치 또는 발사 위치)을 고른다.
    /// </summary>
    private static Dictionary<string, HeatmapBounds> ComputeHeatmapBounds(IEnumerable<SessionDigest> digests, Func<RaidDigest, List<float>> pointsOf)
    {
        Dictionary<string, HeatmapBounds> bounds = new(StringComparer.Ordinal);
        foreach (RaidDigest raid in digests.SelectMany(d => d.Raids))
        {
            List<float> points = pointsOf(raid);
            if (points.Count == 0)
            {
                continue;
            }

            if (!bounds.TryGetValue(raid.Scene, out HeatmapBounds? b))
            {
                b = new HeatmapBounds();
                bounds[raid.Scene] = b;
            }

            for (int i = 0; i + 1 < points.Count; i += 2)
            {
                b.Include(points[i], points[i + 1]);
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
        Dictionary<string, TimelineAccumulator?[]> stageTimelines = new(StringComparer.Ordinal);
        Dictionary<(string Scene, int? Day), int[]> heat = new();
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
            v.OrdersCompleted += d.OrdersCompleted;
            v.OrdersSucceeded += d.OrdersSucceeded;
            v.CustomerDeaths += d.CustomerDeaths;
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

            if (!stageTimelines.TryGetValue(raid.Scene, out TimelineAccumulator?[]? stageTimeline))
            {
                stageTimeline = new TimelineAccumulator?[MAX_TIMELINE_BINS];
                stageTimelines[raid.Scene] = stageTimeline;
            }

            foreach (KeyValuePair<int, TimelineAccumulator> pair in raid.Timeline)
            {
                (timeline[pair.Key] ??= new TimelineAccumulator()).Merge(pair.Value);
                (stageTimeline[pair.Key] ??= new TimelineAccumulator()).Merge(pair.Value);
            }

            if (raid.Points.Count > 0)
            {
                // 전체 일차 합계(Day = null)와, 일차를 아는 습격은 그 일차 격자에도 더한다
                HeatmapBounds b = bounds[raid.Scene];
                foreach (int? key in raid.Day.HasValue ? new int?[] { null, raid.Day } : new int?[] { null })
                {
                    if (!heat.TryGetValue((raid.Scene, key), out int[]? cells))
                    {
                        cells = new int[HEATMAP_GRID * HEATMAP_GRID];
                        heat[(raid.Scene, key)] = cells;
                    }

                    for (int i = 0; i + 1 < raid.Points.Count; i += 2)
                    {
                        cells[b.Row(raid.Points[i + 1], HEATMAP_GRID) * HEATMAP_GRID + b.Col(raid.Points[i], HEATMAP_GRID)]++;
                    }
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

        v.Timeline = TimelinePoints(timeline);
        v.StageTimelines = stageTimelines
            .Select(p => new StageTimeline { Scene = p.Key, Points = TimelinePoints(p.Value) })
            .Where(t => t.Points.Count > 0)
            .OrderBy(t => t.Scene, StringComparer.Ordinal)
            .ToList();

        foreach (KeyValuePair<(string Scene, int? Day), int[]> pair in heat
            .OrderBy(p => p.Key.Scene, StringComparer.Ordinal).ThenBy(p => p.Key.Day ?? -1))
        {
            HeatmapBounds b = bounds[pair.Key.Scene];
            v.Heatmaps.Add(new Heatmap
            {
                StageId = pair.Key.Scene,
                Day = pair.Key.Day,
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
    /// 한 버전에 속한 세션 요약의 총기 기록을 합산한다. 구성은 Key(게임 구성 ID)로 합치고 빌드 수 내림차순으로 둔다.
    /// 습격 발사는 습격 구간의 스테이지로 나눠 구성별 StageShots와 스테이지별 발사 위치 히트맵에 더한다.
    /// </summary>
    private static WeaponVersionStats AggregateWeapons(
        string version,
        List<(SessionRow Row, SessionDigest Digest)> sessions,
        Dictionary<string, HeatmapBounds> bounds)
    {
        WeaponVersionStats v = new() { Version = version, Sessions = sessions.Count };
        Dictionary<string, WeaponConfig> configs = new(StringComparer.Ordinal);
        Dictionary<string, int[]> heat = new(StringComparer.Ordinal);

        // 구성 ID만 본 세션(Known=false)이 먼저 와도, 코드를 본 세션이 나오면 파츠 정보를 채운다.
        WeaponConfig Total(WeaponConfig c)
        {
            if (!configs.TryGetValue(c.Key, out WeaponConfig? total))
            {
                total = new WeaponConfig { Key = c.Key };
                configs[c.Key] = total;
            }

            if (!total.Known && c.Known)
            {
                total.Known = true;
                total.Tutorial = c.Tutorial;
                total.Parts = c.Parts;
                total.Blocks = c.Blocks;
            }

            return total;
        }

        foreach ((_, SessionDigest digest) in sessions)
        {
            WeaponDigest w = digest.Weapons;
            v.WorkbenchExits += w.WorkbenchExits;
            v.RepeatExits += w.RepeatExits;
            v.ExportFailed += w.ExportFailed;
            v.DecodeFailed += w.DecodeFailed;
            v.CustomNamedBuilds += w.CustomNamedBuilds;
            v.ColoredBuilds += w.ColoredBuilds;
            v.Sales += digest.Sales;
            v.SalesWithCode += w.SalesWithCode;
            v.SalesInferred += w.SalesInferred;
            v.AttackInputs += w.AttackInputs;
            v.AttackInputsDecoded += w.AttackInputsDecoded;
            v.ShopShots += w.ShopShots;
            v.ShopShotsWithoutId += w.ShopShotsWithoutId;
            int sessionBuilds = 0;
            foreach (WeaponConfig c in w.Configs.Values)
            {
                WeaponConfig total = Total(c);
                total.Builds += c.Builds;
                total.Sessions += c.Builds > 0 ? 1 : 0;
                total.Sold += c.Sold;
                total.PriceSum += c.PriceSum;
                total.Priced += c.Priced;
                total.Satisfied += c.Satisfied;
                total.SatisfiedKnown += c.SatisfiedKnown;
                total.AttackInputs += c.AttackInputs;
                total.ShopShots += c.ShopShots;
                total.RaidLoadouts += c.RaidLoadouts;
                sessionBuilds += c.Builds;
            }

            v.Builds += sessionBuilds;
            v.SessionsWithBuilds += sessionBuilds > 0 ? 1 : 0;

            foreach (RaidDigest raid in digest.Raids)
            {
                v.Raids++;
                v.RaidsWithLoadout += raid.HasLoadout ? 1 : 0;
                if (raid.LoadoutValue is { } value)
                {
                    v.RaidValues.Add(new RaidWeaponValue
                    {
                        Stage = raid.Scene,
                        Day = raid.Day,
                        Survived = raid.Survived,
                        Dollars = value.Dollars,
                        Credits = value.Credits,
                    });
                }
                v.RaidWeaponChanges += raid.WeaponChanges;
                v.RaidShotsWithoutId += raid.ShotsWithoutId;
                v.RaidShots += raid.ShotsWithoutId;
                foreach ((string key, int shots) in raid.Shots)
                {
                    // 습격 발사의 구성 ID는 WeaponTracker가 세션 요약에 등록해 둔다(코드를 못 봤으면 Known=false).
                    WeaponConfig total = Total(w.Configs[key]);
                    total.RaidShots += shots;
                    total.StageShots.TryGetValue(raid.Scene, out int stageShots);
                    total.StageShots[raid.Scene] = stageShots + shots;
                    v.RaidShots += shots;
                }

                if (raid.ShotPoints.Count == 0)
                {
                    continue;
                }

                HeatmapBounds b = bounds[raid.Scene];
                if (!heat.TryGetValue(raid.Scene, out int[]? cells))
                {
                    cells = new int[HEATMAP_GRID * HEATMAP_GRID];
                    heat[raid.Scene] = cells;
                }

                for (int i = 0; i + 1 < raid.ShotPoints.Count; i += 2)
                {
                    cells[b.Row(raid.ShotPoints[i + 1], HEATMAP_GRID) * HEATMAP_GRID + b.Col(raid.ShotPoints[i], HEATMAP_GRID)]++;
                }
            }
        }

        v.SalesLinked = v.SalesWithCode + v.SalesInferred;
        v.Configs = configs.Values
            .OrderByDescending(c => c.Builds).ThenByDescending(c => c.RaidShots + c.ShopShots).ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
        foreach (KeyValuePair<string, int[]> pair in heat.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            HeatmapBounds b = bounds[pair.Key];
            v.ShotHeatmaps.Add(new Heatmap
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

    /// <summary>불리언 필드를 읽는다. 없거나 불리언이 아니면 null.</summary>
    private static bool? ReadBool(JsonElement data, string name)
    {
        if (TryGet(data, name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.ValueKind == JsonValueKind.True;
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
    public int OrdersCompleted;
    public int OrdersSucceeded;
    public int CustomerDeaths;
    public readonly List<double> WorkbenchSeconds = new();
    public int UnmatchedWorkbench;
    public readonly List<RaidDigest> Raids = new();
    public readonly WeaponDigest Weapons = new();
    public readonly InsightDigest Insights = new();
}

/// <summary>한 세션의 총기 기록 요약. Configs는 구성 Key별 합계다(Sessions 필드는 버전 합산 때 채운다).</summary>
internal sealed class WeaponDigest
{
    public int WorkbenchExits;
    public int RepeatExits;
    public int ExportFailed;
    public int DecodeFailed;
    public int CustomNamedBuilds;
    public int ColoredBuilds;
    public int SalesWithCode;
    public int SalesInferred;
    public int AttackInputs;
    public int AttackInputsDecoded;
    public int ShopShots;
    public int ShopShotsWithoutId;
    public readonly Dictionary<string, WeaponConfig> Configs = new(StringComparer.Ordinal);
}

/// <summary>
/// 세션 하나를 시간순으로 훑으며 총기 이벤트를 WeaponDigest에 쌓는다. 구성은 게임 구성 ID(weaponAssemblyId)로 묶는다.
/// 빌드는 작업대 퇴장 때의 구성이며, 같은 orderId의 직전 퇴장과 구성이 같으면 빌드로 세지 않는다(구성 변경 없는 재퇴장).
/// 판매는 weapon_sold의 코드(실제 판매 총)를 쓰고, 코드가 없는 이전 빌드 로그만 같은 orderId의 마지막 빌드로 추정한다.
/// 같은 코드는 세션 안에서 한 번만 해석한다.
/// </summary>
internal sealed class WeaponTracker
{
    private readonly WeaponDigest _digest;
    private readonly Dictionary<string, WeaponLayout?> _decoded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastBuildByOrder = new(StringComparer.Ordinal);

    public WeaponTracker(WeaponDigest digest)
    {
        _digest = digest;
    }

    public void WorkbenchExit(JsonElement data)
    {
        _digest.WorkbenchExits++;
        WeaponLayout? layout = Layout(data, out bool exported);
        if (!exported)
        {
            _digest.ExportFailed++;
            return;
        }

        if (layout == null)
        {
            _digest.DecodeFailed++;
            return;
        }

        string order = Text(data, "orderId");
        if (_lastBuildByOrder.TryGetValue(order, out string? previous) && previous == layout.Key)
        {
            _digest.RepeatExits++;
            return;
        }

        _lastBuildByOrder[order] = layout.Key;
        Config(layout).Builds++;
        _digest.CustomNamedBuilds += layout.CustomName ? 1 : 0;
        _digest.ColoredBuilds += layout.Blocks.Any(b => b.HasColor) ? 1 : 0;
    }

    public void AttackInput(JsonElement data)
    {
        _digest.AttackInputs++;
        if (Layout(data, out _) is { } layout)
        {
            _digest.AttackInputsDecoded++;
            Config(layout).AttackInputs++;
        }
    }

    /// <summary>공방 주간의 실제 발사(Shop / weapon_fired). 코드가 없으므로 구성 ID로만 묶는다.</summary>
    public void ShopShot(JsonElement data)
    {
        string key = AssemblyId(data);
        if (key.Length == 0)
        {
            _digest.ShopShotsWithoutId++;
            return;
        }

        _digest.ShopShots++;
        ConfigById(key).ShopShots++;
    }

    public void Sale(JsonElement data)
    {
        WeaponConfig? config = null;
        if (Layout(data, out _) is { } sold)
        {
            config = Config(sold);
            _digest.SalesWithCode++;
        }
        else if (Text(data, "weaponCode").Length == 0)
        {
            string order = Text(data, "orderId");
            if (order.Length > 0 && _lastBuildByOrder.TryGetValue(order, out string? key))
            {
                config = _digest.Configs[key];
                _digest.SalesInferred++;
            }
        }

        if (config == null)
        {
            return;
        }

        config.Sold++;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("price", out JsonElement price)
            && price.ValueKind == JsonValueKind.Number && price.TryGetDouble(out double p) && double.IsFinite(p))
        {
            config.PriceSum += p;
            config.Priced++;
        }

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("satisfied", out JsonElement s)
            && s.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            config.SatisfiedKnown++;
            config.Satisfied += s.ValueKind == JsonValueKind.True ? 1 : 0;
        }
    }

    /// <summary>
    /// 습격 구간 안의 총기 이벤트. 습격 시작은 휴대 총(코드·구성 ID), raid_weapon_config는 습격 중 처음 쓰는 구성,
    /// weapon_fired는 실제 발사(구성 ID·위치)다.
    /// </summary>
    public void RaidEvent(RaidDigest raid, string name, JsonElement data)
    {
        switch (name)
        {
            case GameEvents.RaidStart:
                if (Layout(data, out _) is { } loadout)
                {
                    raid.HasLoadout = true;
                    raid.LoadoutValue = loadout.Value();
                    Config(loadout).RaidLoadouts++;
                }

                break;

            case GameEvents.RaidWeaponConfig:
                raid.WeaponChanges++;
                if (Layout(data, out _) is { } changed)
                {
                    Config(changed);
                }

                break;

            case GameEvents.WeaponFired:
                string key = AssemblyId(data);
                if (key.Length == 0)
                {
                    raid.ShotsWithoutId++;
                }
                else
                {
                    ConfigById(key);
                    raid.Shots.TryGetValue(key, out int shots);
                    raid.Shots[key] = shots + 1;
                }

                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("position", out JsonElement position)
                    && position.ValueKind == JsonValueKind.Object
                    && position.TryGetProperty("x", out JsonElement x) && x.ValueKind == JsonValueKind.Number
                    && position.TryGetProperty("z", out JsonElement z) && z.ValueKind == JsonValueKind.Number)
                {
                    raid.ShotPoints.Add((float)x.GetDouble());
                    raid.ShotPoints.Add((float)z.GetDouble());
                }

                break;
        }
    }

    /// <summary>weaponCode를 해석한다. exported는 게임이 코드를 만들었는지(weaponCodeStatus=ok이고 코드가 있음)다.</summary>
    private WeaponLayout? Layout(JsonElement data, out bool exported)
    {
        string code = Text(data, "weaponCode");
        string status = Text(data, "weaponCodeStatus");
        exported = code.Length > 0 && (status.Length == 0 || status == "ok");
        if (!exported)
        {
            return null;
        }

        if (!_decoded.TryGetValue(code, out WeaponLayout? layout))
        {
            layout = WeaponCode.TryDecode(code);
            _decoded[code] = layout;
        }

        return layout;
    }

    /// <summary>코드를 해석한 구성을 등록한다. 구성 ID만 보고 먼저 만든 항목이면 파츠 정보를 채운다.</summary>
    private WeaponConfig Config(WeaponLayout layout)
    {
        WeaponConfig config = ConfigById(layout.Key);
        if (!config.Known)
        {
            config.Known = true;
            config.Tutorial = layout.IsTutorialDefault;
            config.Parts = layout.PartCounts();
            config.Blocks = layout.Blocks.Count;
        }

        return config;
    }

    private WeaponConfig ConfigById(string key)
    {
        if (!_digest.Configs.TryGetValue(key, out WeaponConfig? config))
        {
            config = new WeaponConfig { Key = key };
            _digest.Configs[key] = config;
        }

        return config;
    }

    /// <summary>weaponAssemblyId. 상태가 ok가 아니면 빈 문자열.</summary>
    private static string AssemblyId(JsonElement data)
    {
        string status = Text(data, "weaponAssemblyIdStatus");
        return status.Length == 0 || status == "ok" ? Text(data, "weaponAssemblyId") : string.Empty;
    }

    private static string Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}

/// <summary>습격 한 구간(습격 시작 ~ 종료 이벤트)의 요약.</summary>
internal sealed class RaidDigest
{
    public bool HasStart;
    public string Scene = string.Empty;
    public int? Day;
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

    // 고스트 리플레이 — Track은 [시각, x, z, 시선(도), 체력] 0.2초 간격, ShotTrack·HitTrack은 [시각, x, z], KillTimes는 적 처치 시각
    public string Exit = string.Empty;
    public double LastTrackTime = double.NegativeInfinity;
    public readonly List<float> Track = new();
    public readonly List<float> ShotTrack = new();
    public readonly List<float> HitTrack = new();
    public readonly List<float> KillTimes = new();

    // 총기 — 습격 시작의 휴대 총, 습격 중 구성 교체, 실제 발사(구성 ID별 수와 x·z 위치)
    public bool HasLoadout;

    /// <summary>습격 시작 휴대 총의 가치(달러, 모듈 재화). 휴대 총 코드가 없으면 null.</summary>
    public (int Dollars, int Credits)? LoadoutValue;
    public int WeaponChanges;
    public readonly Dictionary<string, int> Shots = new(StringComparer.Ordinal);
    public int ShotsWithoutId;
    public readonly List<float> ShotPoints = new();
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
