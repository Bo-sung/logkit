using System.Text.Json;

namespace LogServer;

/// <summary>
/// 가설·일반 통계(/insights)용 세션 기록 수집기. 세션 하나를 시간순으로 훑으며 일차·주문·습격 단위 기록을 만든다.
/// 일차는 Shop 이벤트의 day 필드로 정하고, 습격은 시작 직전에 본 일차의 밤으로 본다. day가 없는 초기 빌드 로그는 일차 기록에서 빠진다.
/// 주문의 수정 횟수는 같은 orderId의 작업대 퇴장 중 구성이 바뀐 횟수(WeaponTracker의 빌드와 같은 규칙)다.
/// 낮·밤 구간: n일차 낮은 day=n인 첫 상점 이벤트부터 그날 첫 습격 시작까지, 밤은 그 습격 시작부터 다른 day의 첫 상점 이벤트까지다.
/// 구간 시간은 실제 시각 차에서 일시정지(다음 이벤트까지)를 뺀 값이며, 세션의 마지막 구간은 Ended(이탈 구간)로 표시한다.
/// 기록마다 그때의 saveId(System 이벤트)를 붙여 두고, 버전 집계(InsightPlayers)가 세이브 단위로 세션을 잇는다.
/// </summary>
internal sealed class InsightTracker
{
    private readonly InsightDigest _digest;
    private readonly Dictionary<string, WeaponLayout?> _decoded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InsightOrder> _orders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastBuildByOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<int, InsightDay> _days = new();
    private (DateTime At, string Order)? _openWorkbench;
    private InsightRaid? _raid;
    private Dictionary<string, double> _spotted = new(StringComparer.Ordinal);
    private List<double> _responses = new();
    private int? _day;
    private InsightPhase? _phase;
    private DateTime _phaseStart;
    private double _phasePaused;
    private DateTime? _pauseStart;
    private DateTime? _raidStart;
    private DateTime _lastAt;
    private string _saveId = string.Empty;
    private InsightTutorial? _tutorial;
    private (int Day, double Balance)? _closeBalance;
    private readonly Dictionary<string, double> _saveSeconds = new(StringComparer.Ordinal);

    public InsightTracker(InsightDigest digest)
    {
        _digest = digest;
    }

    /// <summary>모든 행에서 먼저 부른다. 열린 일시정지를 이 행 시각에서 닫고, 일시정지 행이면 새로 연다.</summary>
    public void Observe(DateTime at, string category, string name)
    {
        if (_pauseStart == null && _lastAt != default && at > _lastAt)
        {
            // 세이브별 플레이 시간(일시정지 제외). 엔딩까지 누적 플레이 시간에 쓴다.
            _saveSeconds[_saveId] = SaveSeconds() + (at - _lastAt).TotalSeconds;
        }

        _lastAt = at;
        if (_pauseStart is DateTime paused)
        {
            _phasePaused += Math.Max(0, (at - paused).TotalSeconds);
            _pauseStart = null;
        }

        if (category == GameEvents.GameCategory && name == GameEvents.Pause)
        {
            _pauseStart = at;
        }
    }

    /// <summary>System·Game·Tutorial 이벤트. saveId를 갱신하고 파산과 튜토리얼 진행을 적는다.</summary>
    public void Other(string category, string name, JsonElement data)
    {
        string save = Text(data, "saveId");
        if (save.Length > 0)
        {
            _saveId = save;
        }

        if (category == GameEvents.GameCategory && name == GameEvents.Bankrupt && _phase != null)
        {
            _phase.Bankrupt = true;
        }

        if (category == GameEvents.GameCategory)
        {
            switch (name)
            {
                case GameEvents.RetireCompleted:
                    InsightEnding retired = Ending("retire", data);
                    retired.Balance = Number(data, "balanceBefore");
                    retired.BalanceAfter = Number(data, "balanceAfter");
                    retired.Loans = Number(data, "outstandingLoans");
                    retired.Credits = Number(data, "moduleCredits");
                    retired.Endless = Bool(data, "endless");
                    retired.First = Bool(data, "firstEnding");
                    break;

                case GameEvents.RetireFailed:
                    InsightEnding failed = Ending("retire_failed", data);
                    failed.Reason = Text(data, "reason");
                    failed.Balance = Number(data, "balance");
                    break;

                case GameEvents.RaidFirstClear:
                case GameEvents.RaidSkipped:
                    bool skipped = name == GameEvents.RaidSkipped;
                    _digest.Nights.Add(new InsightNight
                    {
                        Kind = skipped ? "skipped" : "first_clear",
                        Day = Number(data, "day") is double nightDay && nightDay >= 0 ? (int)nightDay : _day,
                        Stage = skipped ? null : Text(data, "scene"),
                        BonusAdded = skipped ? null : Number(data, "saleBonusAdded"),
                        BonusTotal = skipped ? null : Number(data, "saleBonusTotal"),
                        Credits = skipped ? Number(data, "moduleCreditsGranted") : null,
                        SaveId = _saveId,
                    });
                    break;

                case GameEvents.EndingChoice:
                    InsightEnding choice = Ending("choice", data);
                    choice.Type = Text(data, "endingType");
                    choice.Choice = Text(data, "choice");
                    choice.Panel = Number(data, "panelSeconds");
                    choice.First = Bool(data, "firstEnding");
                    break;
            }
        }

        if (category == GameEvents.TutorialCategory)
        {
            Tutorial(name, data);
        }
    }

    /// <summary>
    /// 튜토리얼 이벤트. 같은 sequence의 단계가 끝(tutorial_ended·tutorial_skipped) 없이 이어지면 한 번의 진행으로 묶는다.
    /// debugSkipped(에디터 F3 넘기기)가 하나라도 있으면 Debug로 표시해 통계에서 뺀다.
    /// </summary>
    private void Tutorial(string name, JsonElement data)
    {
        string sequence = Text(data, "sequence");
        switch (name)
        {
            case GameEvents.TutorialStepStarted:
                Step(TutorialRun(sequence), data);
                break;

            case GameEvents.TutorialStepCompleted:
                InsightTutorial run = TutorialRun(sequence);
                InsightTutorialStep step = Step(run, data);
                step.Completed = true;
                step.Duration = Number(data, "duration") ?? step.Duration;
                run.Debug |= Bool(data, "debugSkipped") == true;
                break;

            case GameEvents.TutorialSkipped:
                TutorialRun(sequence).Outcome = "skipped";
                _tutorial = null;
                break;

            case GameEvents.TutorialEnded:
                string reason = Text(data, "reason");
                TutorialRun(sequence).Outcome = reason.Length > 0 ? reason : "ended";
                _tutorial = null;
                break;
        }
    }

    private double SaveSeconds() => _saveSeconds.TryGetValue(_saveId, out double seconds) ? seconds : 0;

    /// <summary>엔딩 기록 하나를 만들어 넣는다. 일차는 이벤트의 day(없거나 음수면 현재 일차), Play는 이 세션의 그 세이브 플레이 시간이다.</summary>
    private InsightEnding Ending(string kind, JsonElement data)
    {
        InsightEnding ending = new()
        {
            Kind = kind,
            Day = Number(data, "day") is double day && day >= 0 ? (int)day : _day,
            Play = SaveSeconds(),
            SaveId = _saveId,
        };
        _digest.Endings.Add(ending);
        return ending;
    }

    /// <summary>NightRaid 엔드 상자 이벤트(스캔·명중·열림). 습격 시작 후 시간(duration)과 상자 조건·탄 수치를 적는다.</summary>
    private void EndChest(string name, JsonElement data)
    {
        string kind = name switch
        {
            GameEvents.EndChestScanned => "chest_scanned",
            GameEvents.EndChestHit => "chest_hit",
            GameEvents.EndChestOpened => "chest_opened",
            _ => string.Empty,
        };
        if (kind.Length == 0)
        {
            return;
        }

        InsightEnding chest = Ending(kind, data);
        chest.Day ??= _raid?.Day;
        chest.Stage = Text(data, "scene");
        chest.Raid = Number(data, "duration");
        chest.Gravity = Number(data, kind == "chest_scanned" ? "requiredGravity" : "gravity");
        chest.Homing = Number(data, kind == "chest_scanned" ? "requiredHoming" : "homing");
        chest.Scanned = Bool(data, "scanned");
        chest.Absorb = Number(data, "absorbSeconds");
    }

    private InsightTutorial TutorialRun(string sequence)
    {
        if (_tutorial == null || _tutorial.Sequence != sequence)
        {
            _tutorial = new InsightTutorial { Sequence = sequence, SaveId = _saveId };
            _digest.Tutorials.Add(_tutorial);
        }

        return _tutorial;
    }

    private static InsightTutorialStep Step(InsightTutorial run, JsonElement data)
    {
        int index = Number(data, "stepIndex") is double i ? (int)i : -1;
        string id = Text(data, "stepId");
        InsightTutorialStep? step = run.Steps.Find(x => x.Index == index && x.Id == id);
        if (step == null)
        {
            step = new InsightTutorialStep { Index = index, Id = id };
            run.Steps.Add(step);
        }

        return step;
    }

    /// <summary>Shop 이벤트 하나. 모든 이벤트의 day로 현재 일차를 갱신하고, 일차가 바뀌면 새 낮 구간을 연다.</summary>
    public void Shop(DateTime at, string name, JsonElement data)
    {
        if (Number(data, "day") is double day && day >= 0 && day < 1000)
        {
            int today = (int)day;
            if (_phase == null || _phase.Day != today)
            {
                StartPhase(at, today, night: false);
            }

            _day = today;
        }

        string orderId = Text(data, "orderId");
        switch (name)
        {
            case GameEvents.ShopOpened:
                if (_phase is { Night: false } && Number(data, "balance") is double opened)
                {
                    _phase.Balance ??= opened;
                }

                break;

            case GameEvents.ShopCloseRequested:
                if (_day is int closedDay && Number(data, "balance") is double closed)
                {
                    _closeBalance = (closedDay, closed);
                }

                break;

            case GameEvents.OrderCreated:
            case GameEvents.OrderRestored:
                if (Order(orderId) is { } placed)
                {
                    placed.SetNpc(Text(data, "profileId"));
                    if (Strings(data, "requestIds") is { } requests)
                    {
                        placed.Requests = requests;
                    }
                }

                break;

            case GameEvents.SlotSpin:
                _digest.Spins.Add(new InsightSpin
                {
                    Day = Number(data, "day") is double spinDay && spinDay >= 0 ? (int)spinDay : _day,
                    Night = Bool(data, "isNight") == true,
                    Pool = Text(data, "oddsPool"),
                    Before = Number(data, "balanceBefore"),
                    Price = Number(data, "price") ?? 0,
                    Prize = Number(data, "plannedPrize") ?? 0,
                    Symbol = Text(data, "plannedSymbol"),
                    SaveId = _saveId,
                });
                break;

            case GameEvents.WorkbenchEnter:
                _openWorkbench = orderId.Length > 0 ? (at, orderId) : null;
                break;

            case GameEvents.WorkbenchExit:
                InsightOrder? order = Order(orderId);
                if (order != null && _openWorkbench is { } open && open.Order == orderId && at >= open.At)
                {
                    order.WorkbenchSeconds = (order.WorkbenchSeconds ?? 0) + (at - open.At).TotalSeconds;
                }

                _openWorkbench = null;
                if (Layout(data) is not { } layout)
                {
                    break;
                }

                if (_lastBuildByOrder.TryGetValue(orderId, out string? previous) && previous == layout.Key)
                {
                    break;
                }

                _lastBuildByOrder[orderId] = layout.Key;
                if (order != null)
                {
                    order.Builds++;
                    order.BuildsAfterTest += order.Tests > 0 ? 1 : 0;
                }

                if (Today() is { } today)
                {
                    today.Builds.Add(layout.PartCounts());
                    today.BuildRequests.Add(order?.Requests ?? Array.Empty<string>());
                }

                break;

            case GameEvents.AttackInput:
                if (Order(orderId) is { } tested)
                {
                    tested.Tests++;
                }

                break;

            case GameEvents.Sale:
            case GameEvents.LegacySale:
                double? price = Number(data, "price");
                bool? satisfied = Bool(data, "satisfied");
                if (Order(orderId) is { } sold)
                {
                    sold.Sold = true;
                    sold.Price = price;
                    sold.Satisfied = satisfied;
                }

                if (Today() is { } saleDay)
                {
                    saleDay.Sales++;
                    saleDay.SalesSum += price ?? 0;
                    saleDay.Priced += price.HasValue ? 1 : 0;
                    saleDay.Satisfied += satisfied == true ? 1 : 0;
                }

                break;
        }
    }

    /// <summary>습격 시작. 그날 밤 구간을 열고(이미 밤이면 이어 감), 휴대 총 코드가 있으면 그 파츠를 습격 총으로 둔다.</summary>
    public void RaidStart(DateTime at, string scene, JsonElement data)
    {
        FinishRaid();
        if (_day is int day && (_phase == null || !_phase.Night))
        {
            StartPhase(at, day, night: true);
        }

        _raidStart = at;
        // 습격 시작의 day(출발한 공방의 일차, 습격 씬 직접 실행은 -1)가 있으면 그 값을 쓴다
        int? raidDay = Number(data, "day") is double d && d >= 0 ? (int)d : _day;
        _raid = new InsightRaid
        {
            Day = raidDay,
            Stage = scene,
            SaveId = _saveId,
            Penetration = Number(data, "penetration"),
            Explosion = Number(data, "explosion"),
            EntryCost = Number(data, "entryCost"),
            Training = Bool(data, "training"),
            SpawnedModule = Number(data, "spawnedModuleCrates"),
            SpawnedMaterial = Number(data, "spawnedMaterialCrates"),
            ReplacedModule = Number(data, "replacedModuleSpots"),
            MaterialPerCrate = Number(data, "materialPerCrate"),
        };
        _digest.Raids.Add(_raid);
        if (Layout(data) is { } loadout)
        {
            _raid.Parts = loadout.PartCounts();
            _raid.Gun = "loadout";
        }
    }

    /// <summary>습격 구간 안의 이벤트. 시작 없이 온 이벤트는 새 구간을 연다(GameAnalytics와 같은 규칙).</summary>
    public void RaidEvent(string scene, string name, JsonElement data)
    {
        if (_raid == null)
        {
            _raid = new InsightRaid { Day = _day, Stage = scene, SaveId = _saveId };
            _digest.Raids.Add(_raid);
        }

        if (_raid.Stage.Length == 0)
        {
            _raid.Stage = scene;
        }

        EndChest(name, data);
        string enemy = Text(data, "enemyId");
        double? time = Number(data, "time");
        switch (name)
        {
            case GameEvents.EnemySpotted:
                if (enemy.Length > 0 && time is double spottedAt)
                {
                    _spotted.TryAdd(enemy, spottedAt);
                }

                break;

            case GameEvents.EnemyKilled:
                if (enemy.Length > 0 && time is double killedAt && _spotted.Remove(enemy, out double seenAt) && killedAt >= seenAt)
                {
                    _responses.Add(killedAt - seenAt);
                }

                break;

            case GameEvents.WeaponFired:
                string key = Text(data, "weaponAssemblyIdStatus") is "" or "ok" ? Text(data, "weaponAssemblyId") : string.Empty;
                if (key.Length > 0)
                {
                    _raid.Shots.TryGetValue(key, out int shots);
                    _raid.Shots[key] = shots + 1;
                }

                break;
        }
    }

    /// <summary>습격 종료 이벤트. 성공 여부와 보상 크레딧을 적고 구간을 닫는다.</summary>
    public void RaidEnd(DateTime at, JsonElement data)
    {
        if (_phase is { Night: true } && _raidStart is DateTime started && at >= started)
        {
            _phase.RaidSeconds = (_phase.RaidSeconds ?? 0) + (at - started).TotalSeconds;
            _phase.LastRaid = Bool(data, "survived") switch { true => "success", false => "fail", _ => string.Empty };
        }

        _raidStart = null;
        if (_raid == null)
        {
            return;
        }

        _raid.Survived = Bool(data, "survived");
        _raid.Exit = Text(data, "exitReason");
        _raid.Duration = Number(data, "duration");
        _raid.Kills = Number(data, "enemyKills");
        _raid.Dollar = Number(data, "dollarReward");
        _raid.Applied = Bool(data, "rewardsApplied");
        _raid.Material = Number(data, "materialCrateCredits");
        _raid.Pickups = Number(data, "materialPickups");
        _raid.Training ??= Bool(data, "training");
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("moduleCrateRewards", out JsonElement rewards)
            && rewards.ValueKind == JsonValueKind.Array)
        {
            _raid.Rewards = rewards.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
                .Select(x => x.GetInt32())
                .ToList();
        }

        double? module = Number(data, "moduleCrateCredits"), material = Number(data, "materialCrateCredits");
        _raid.Credits = module.HasValue || material.HasValue ? (module ?? 0) + (material ?? 0) : null;
        FinishRaid();
    }

    /// <summary>
    /// 세션 끝 정리. 휴대 총 코드가 없는 습격은 그 습격에서 가장 많이 쏜 구성 중 이 세션에서 코드를 본 것을 습격 총으로 쓰고,
    /// 실패한 습격마다 이후 다른 습격이 있었는지(재도전)를 적는다.
    /// </summary>
    public void Finish(WeaponDigest weapons)
    {
        FinishRaid();
        if (_phase != null)
        {
            ClosePhase(_lastAt);
            _digest.Phases[^1].Ended = true;
        }

        foreach (InsightRaid raid in _digest.Raids)
        {
            if (raid.Stage.Length == 0)
            {
                raid.Stage = "(미기록)";
            }

            if (raid.Parts != null)
            {
                continue;
            }

            foreach ((string key, _) in raid.Shots.OrderByDescending(p => p.Value))
            {
                if (weapons.Configs.TryGetValue(key, out WeaponConfig? config) && config.Known)
                {
                    raid.Parts = new Dictionary<string, int>(config.Parts, StringComparer.Ordinal);
                    raid.Gun = "fired";
                    break;
                }
            }
        }

        for (int i = 0; i < _digest.Raids.Count; i++)
        {
            InsightRaid raid = _digest.Raids[i];
            if (raid.Survived != false)
            {
                continue;
            }

            List<InsightRaid> later = _digest.Raids.Skip(i + 1).ToList();
            raid.Retried = later.Count > 0;
            raid.RetriedSameStage = later.Any(r => r.Stage == raid.Stage);
        }

        _digest.SaveSeconds = new Dictionary<string, double>(_saveSeconds, StringComparer.Ordinal);
        _digest.Orders.AddRange(_orders.Values.Where(o => o.Day != null));
        _digest.Days.AddRange(_days.OrderBy(p => p.Key).Select(p => p.Value));
    }

    /// <summary>열린 구간을 at에서 닫고 새 구간을 연다.</summary>
    private void StartPhase(DateTime at, int day, bool night)
    {
        ClosePhase(at);
        _phase = new InsightPhase { Day = day, Night = night, SaveId = _saveId };
        if (night && _closeBalance is { } close && close.Day == day)
        {
            _phase.Balance = close.Balance;
        }

        _phaseStart = at;
        _phasePaused = 0;
    }

    private void ClosePhase(DateTime at)
    {
        if (_phase == null)
        {
            return;
        }

        _phase.Seconds = Math.Max(0, (at - _phaseStart).TotalSeconds - _phasePaused);
        _digest.Phases.Add(_phase);
        _phase = null;
    }

    private void FinishRaid()
    {
        if (_raid != null && _responses.Count > 0)
        {
            _raid.ResponseMean = _responses.Average();
            _raid.ResponseCount = _responses.Count;
        }

        _raid = null;
        _spotted = new Dictionary<string, double>(StringComparer.Ordinal);
        _responses = new List<double>();
    }

    private InsightDay? Today()
    {
        if (_day is not int day)
        {
            return null;
        }

        if (!_days.TryGetValue(day, out InsightDay? record))
        {
            record = new InsightDay { Day = day, SaveId = _saveId };
            _days[day] = record;
        }

        return record;
    }

    /// <summary>주문 기록을 꺼내거나 만든다. 일차는 처음 본 이벤트의 일차다. orderId가 비면 null.</summary>
    private InsightOrder? Order(string orderId)
    {
        if (orderId.Length == 0)
        {
            return null;
        }

        if (!_orders.TryGetValue(orderId, out InsightOrder? order))
        {
            order = new InsightOrder { Day = _day, OrderId = orderId, SaveId = _saveId };
            _orders[orderId] = order;
        }

        order.Day ??= _day;
        return order;
    }

    private WeaponLayout? Layout(JsonElement data)
    {
        string code = Text(data, "weaponCode");
        string status = Text(data, "weaponCodeStatus");
        if (code.Length == 0 || status.Length > 0 && status != "ok")
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

    private static string Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string[]? Strings(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).ToArray()
            : null;

    private static double? Number(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number)
            ? number
            : null;

    private static bool? Bool(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.ValueKind == JsonValueKind.True
            : null;
}

/// <summary>한 세션의 가설·일반 통계 기록.</summary>
internal sealed class InsightDigest
{
    public readonly List<InsightDay> Days = new();
    public readonly List<InsightOrder> Orders = new();
    public readonly List<InsightRaid> Raids = new();
    public readonly List<InsightPhase> Phases = new();
    public readonly List<InsightTutorial> Tutorials = new();
    public readonly List<InsightSpin> Spins = new();
    public readonly List<InsightEnding> Endings = new();
    public readonly List<InsightNight> Nights = new();

    /// <summary>세이브별 이 세션의 플레이 시간(일시정지 제외, 초).</summary>
    public Dictionary<string, double> SaveSeconds = new(StringComparer.Ordinal);
}

/// <summary>
/// 한 버전의 세션 기록을 플레이어 단위로 합친다. 플레이어 = saveId(같은 세이브를 이어 한 세션들), saveId가 없는 이전 로그는 세션이다.
/// 저장을 불러와 이어 한 경우를 이탈로 세지 않도록 이탈·재도전은 플레이어의 시간순 기록 전체로 다시 정하고,
/// 같은 주문(orderId)·같은 일차·이어지는 같은 구간·끝나지 않은 같은 튜토리얼은 하나로 합친다.
/// </summary>
internal static class InsightPlayers
{
    public static InsightVersion Combine(string version, List<(SessionRow Row, InsightDigest Digest)> sessions)
    {
        InsightVersion v = new() { Version = version, Sessions = sessions.Count };
        Dictionary<string, int> players = new(StringComparer.Ordinal);
        Dictionary<string, InsightOrder> orders = new(StringComparer.Ordinal);
        Dictionary<(int Player, int Day), InsightDay> days = new();
        Dictionary<int, InsightPhase> lastPhase = new();
        Dictionary<int, InsightTutorial> openTutorial = new();
        Dictionary<int, double> played = new();

        for (int s = 0; s < sessions.Count; s++)
        {
            int session = s;
            int Player(string saveId)
            {
                string key = saveId.Length > 0 ? "save:" + saveId : "session:" + session;
                if (!players.TryGetValue(key, out int p))
                {
                    p = players.Count;
                    players[key] = p;
                }

                return p;
            }

            InsightDigest digest = sessions[s].Digest;
            bool clean = sessions[s].Row.EndedCleanly;
            foreach (InsightDay day in digest.Days)
            {
                int p = Player(day.SaveId);
                if (days.TryGetValue((p, day.Day), out InsightDay? same))
                {
                    same.Sales += day.Sales;
                    same.SalesSum += day.SalesSum;
                    same.Priced += day.Priced;
                    same.Satisfied += day.Satisfied;
                    same.Builds.AddRange(day.Builds);
                    same.BuildRequests.AddRange(day.BuildRequests);
                    continue;
                }

                InsightDay copy = day.ForSession(p);
                copy.Builds = new List<Dictionary<string, int>>(day.Builds);
                copy.BuildRequests = new List<string[]>(day.BuildRequests);
                days[(p, day.Day)] = copy;
                v.Days.Add(copy);
            }

            foreach (InsightOrder order in digest.Orders)
            {
                if (orders.TryGetValue(order.OrderId, out InsightOrder? same))
                {
                    same.Merge(order);
                    continue;
                }

                InsightOrder copy = order.ForSession(Player(order.SaveId));
                orders[order.OrderId] = copy;
                v.Orders.Add(copy);
            }

            v.Raids.AddRange(digest.Raids.Select(r => r.ForSession(Player(r.SaveId))));
            v.Spins.AddRange(digest.Spins.Select(x => x.ForSession(Player(x.SaveId), session)));
            v.Nights.AddRange(digest.Nights.Select(x => x.ForSession(Player(x.SaveId))));
            v.ShopSessions += digest.Days.Count > 0 || digest.Phases.Count > 0 ? 1 : 0;

            // 엔딩의 Play = 같은 플레이어의 이전 세션 플레이 시간 합 + 이 세션 안의 시간(세션은 시작 시각 순)
            foreach (InsightEnding ending in digest.Endings)
            {
                int p = Player(ending.SaveId);
                v.Endings.Add(ending.ForSession(p, played.GetValueOrDefault(p) + ending.Play));
            }

            // 기록이 있는 플레이어의 시간만 더한다(세이브를 고르기 전 메뉴 시간이 빈 플레이어를 만들지 않게)
            foreach ((string saveId, double seconds) in digest.SaveSeconds)
            {
                if (players.TryGetValue(saveId.Length > 0 ? "save:" + saveId : "session:" + session, out int p))
                {
                    played[p] = played.GetValueOrDefault(p) + seconds;
                }
            }

            foreach (InsightPhase phase in digest.Phases)
            {
                int p = Player(phase.SaveId);
                if (lastPhase.TryGetValue(p, out InsightPhase? previous) && previous.Day == phase.Day
                    && (previous.Night == phase.Night || previous.Night))
                {
                    // 같은 구간을 다음 세션에서 이어 함(저장 불러오기). 밤에 끄고 불러오면 첫 상점 이벤트가 같은 일차의 낮을 열지만 밤이 이어진 것이다.
                    previous.Seconds += phase.Seconds;
                    previous.RaidSeconds = previous.RaidSeconds.HasValue || phase.RaidSeconds.HasValue
                        ? (previous.RaidSeconds ?? 0) + (phase.RaidSeconds ?? 0)
                        : null;
                    previous.LastRaid = phase.LastRaid.Length > 0 ? phase.LastRaid : previous.LastRaid;
                    previous.Bankrupt |= phase.Bankrupt;
                    previous.Balance ??= phase.Balance;
                    previous.CleanEnd = clean;
                    continue;
                }

                InsightPhase copy = phase.ForSession(p, clean);
                lastPhase[p] = copy;
                v.Phases.Add(copy);
            }

            foreach (InsightTutorial run in digest.Tutorials)
            {
                int p = Player(run.SaveId);
                if (openTutorial.TryGetValue(p, out InsightTutorial? open) && open.Sequence == run.Sequence && open.Outcome.Length == 0)
                {
                    open.Absorb(run);
                }
                else
                {
                    open = run.ForSession(p);
                    v.Tutorials.Add(open);
                }

                openTutorial[p] = open;
            }
        }

        // 이탈 = 플레이어의 마지막 구간, 재도전 = 실패 뒤 같은 플레이어의 습격이 더 있음
        HashSet<InsightPhase> last = new(lastPhase.Values);
        foreach (InsightPhase phase in v.Phases)
        {
            phase.Ended = last.Contains(phase);
        }

        foreach (IGrouping<int, InsightRaid> group in v.Raids.GroupBy(r => r.S))
        {
            List<InsightRaid> list = group.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Survived != false)
                {
                    continue;
                }

                string stage = list[i].Stage;
                List<InsightRaid> later = list.Skip(i + 1).ToList();
                list[i].Retried = later.Count > 0;
                list[i].RetriedSameStage = later.Any(r => r.Stage == stage);
            }
        }

        v.Players = players.Count;
        v.PlayersWithSave = players.Keys.Count(k => k.StartsWith("save:", StringComparison.Ordinal));
        return v;
    }
}

/// <summary>
/// 손님 프로필 ID(CustomerProfile.profileId = 손님 에셋 GUID) → 에셋 이름.
/// 게임 저장소 Assets/Resources/CustomerOrderData.json의 guests(id, name) 기준이다. 없는 ID는 앞 8자리로 표시한다.
/// </summary>
internal static class CustomerNames
{
    private static readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a5308f1435d603f46aea8a8ee70cd591"] = "D0_1_TutorialCustomer",
        ["f6afb57651235a649a138ba28b86b8b3"] = "D0_2_Extension",
        ["8e45417c50eb84d45bc816c2ce4aba9a"] = "D0_2_ModuleCustomer",
        ["e3726adb0bb965747ae2a14bdd32852b"] = "D0_3_SplitTwo",
        ["c2e5e67315c2e684bb468481f9a2bc5b"] = "D0_3_TagCustomer",
        ["6f6cf35ea746e344a93b1587938c685b"] = "D0_4_DifficultCustomer",
        ["1353401e1a4304d4d9e377490fe26882"] = "D0_4_MoreMuzzles",
        ["8df14042c9325de4e9e896078ddebfc2"] = "D0_5_Combo",
        ["25b5381ef8e155343bbafe08fae3af6d"] = "D1_1_BaseCore",
        ["f350a20ea1c6a314b9a2ebfd39e192be"] = "D1_2_Extension",
        ["a660428af7905aa49a0c3400753fd0ae"] = "D1_3_SplitTwo",
        ["19eb28f21e576bd40bdd9363439fb11a"] = "D1_4_MoreMuzzles",
        ["30f765269e0d71446a935b0c88492228"] = "D1_5_Combo",
        ["a00ebe5381d81494ba1d532eaa55a166"] = "D2_1_Shotgun",
        ["d5398f48369de9044b8afcc0e73edc0d"] = "D2_2_Sniper",
        ["54d26364e40dd1b489519863487c0c76"] = "D2_3_Rifle",
        ["d29f7a9b9e4256f449ba2276cf0fa0e4"] = "D2_4_Pistol",
        ["a56c9fc6df6bc184ebb69d9092b8562e"] = "D2_5_BigMac",
        ["a657c8e6daaac0640b26135bf3b0e940"] = "D2_6_Grenade",
        ["e8726eef5e281a241935acf0d3110b60"] = "D2_7_Ricochet 1",
        ["6a41b8901ebccb14a9bea905b5e759ab"] = "D2_7_Ricochet",
        ["2ba099eab2272d940ae22970ff7731f3"] = "D3_1_DoubleBarrelAR",
        ["d8d88852c4557604ebef499f6d5ceda1"] = "D3_2_QuadShotgun",
        ["e09ace2d9e03a6d4b9c5163bfb793a27"] = "D3_3_Mom",
        ["e58d71f88d4f876438bf7acace814a9b"] = "D3_4_Sommelier",
        ["f2b056418a5de7349942f08879971a6f"] = "D3_5_LakePark",
        ["2d9480eb51c562c4c92a1421ea5770da"] = "D3_6_Charge",
        ["1117dd03e64b32a43b93b4a15b89a4c2"] = "D3_7_Gravity",
        ["0c575dbccf9fbcb40a50aa5bf6e458fb"] = "D3_8_Flamer",
        ["038a4f4f9f79c204a9087f7ce969d7f5"] = "D3_9_christ",
        ["d22429fd957afa44483eb23d1c989306"] = "D4_1_HangarCQC",
        ["caed71048059fb84ea75dbd3fa5def6d"] = "D4_2_PolitePistol",
        ["f88c397ed1f539f4a961604044e93feb"] = "D4_3_Robber",
        ["fd770da102b6f294191f246f9ca975f5"] = "D4_4_Professional",
        ["d388b03377bb04d4097c8992aa687d9e"] = "D4_5_RipAndTear",
        ["d5629a5fb5df7874ebefe9e24bd959af"] = "D4_6_Rotation",
        ["89a6e2464d2d3824c92a161cb4ac7990"] = "D4_7_WaterGun",
        ["2d9afc0f00161344c98c8238201fb8ca"] = "D4_8_Homing",
        ["8ca54d168454a8a4bbf7e11f5964cca1"] = "D4_9_Snaiper",
        ["f4e6e6c50ca7d574f8fe2e7cd530a4f6"] = "D5_1_Jjajang",
        ["5867799c1021bd247bdc32c3246d757f"] = "D5_10_jumpBomb",
        ["bb35f40bd3aa17e4f831ca8946978d4b"] = "D5_11_RicsMonkey",
        ["1cd9be92bac19b246923e91f6cf077e7"] = "D5_2_Persona",
        ["3fe84f1ef3ba7c645b20fad83564cada"] = "D5_3_Student",
        ["a0a2b26fb940de64c88d341032fefb96"] = "D5_4_ShotgunCeption",
        ["9f6cc4a608cdb4e4bbe4dcd2b1d6167d"] = "D5_5_RealGun",
        ["0a8af4ee73848804a9693aa05665fb80"] = "D5_6_FlameTornado",
        ["346ffc96cc977c64a8a3730e146a6ad0"] = "D5_7_HomingBlast",
        ["c2bdd0ee67e738841a3edca181024144"] = "D5_8_BlackHole",
        ["52c56089bff6c5341966c99523742ef5"] = "D5_9_Firefighter",
        ["d5a59221e412b7b4bb5188f1953c12d2"] = "D6_1_BankShot",
        ["949c287f545cef84eb20c1f31f8eba01"] = "D6_2_Railgun",
        ["0c5e735d8c860654eb79844236b27b63"] = "D6_3_HomingShotgun",
        ["8f30edea593b9824c88aaa4606b9c50b"] = "D6_4_GrandpaAgain",
        ["c13e2f3309d85a942a71852dc0edb95d"] = "D6_5_Minigun",
        ["64bbf60094050bb4492d018a6e417617"] = "D6_6_Everything",
        ["4dbebd0100c6bc24db1270af6d2e8600"] = "D6_7_RobberAgain",
        ["78f7371a5c003d44393acb6a85257afb"] = "D6_8_Bbotto",
    };

    public static string Name(string profileId) =>
        profileId.Length == 0 ? string.Empty
        : _names.TryGetValue(profileId, out string? name) ? name
        : "손님 " + profileId[..Math.Min(8, profileId.Length)];
}

/// <summary>GET /api/analytics/insights 응답 본문. 버전별 세션 기록 목록이며 화면이 분포·비율을 계산한다.</summary>
public sealed class InsightsResponse
{
    /// <summary>조회한 월(yyyy-MM). 빈 문자열이면 전체 월.</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>에디터 세션을 제외했으면 true.</summary>
    public bool ExcludeEditor { get; set; }

    /// <summary>습격 스테이지 표(게임 RaidMapData 순서). 씬 이름 → 표시 이름.</summary>
    public List<InsightStage> Stages { get; set; } = new();

    /// <summary>버전 오름차순 기록 목록.</summary>
    public List<InsightVersion> Versions { get; set; } = new();
}

/// <summary>습격 스테이지 하나. 순서는 게임 스테이지 표(입장 비용 오름차순)와 같다.</summary>
public sealed class InsightStage
{
    public string Scene { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>현재 씬의 금고 관통 내성(임시 값, 기획 대표 값 미정).</summary>
    public double VaultPenetration { get; set; }
}

/// <summary>버전 하나의 기록. 각 기록의 S는 이 버전 안의 세션 번호(0부터)이며 플레이어 단위로 쓴다.</summary>
public sealed class InsightVersion
{
    public string Version { get; set; } = string.Empty;
    public int Sessions { get; set; }

    /// <summary>플레이어 수(saveId 단위, saveId가 없으면 세션 단위)와 그중 saveId로 묶인 수.</summary>
    public int Players { get; set; }
    public int PlayersWithSave { get; set; }
    public List<InsightDay> Days { get; set; } = new();
    public List<InsightOrder> Orders { get; set; } = new();
    public List<InsightRaid> Raids { get; set; } = new();
    public List<InsightPhase> Phases { get; set; } = new();
    public List<InsightTutorial> Tutorials { get; set; } = new();
    public List<InsightSpin> Spins { get; set; } = new();
    public List<InsightEnding> Endings { get; set; } = new();
    public List<InsightNight> Nights { get; set; } = new();

    /// <summary>상점 일차·구간 기록이 있는 세션 수(슬롯머신 이용률의 분모).</summary>
    public int ShopSessions { get; set; }
}

/// <summary>
/// 습격 밖의 밤 이벤트. Kind = skipped(Game / raid_skipped, 습격 없이 다음 날로 — Credits = 지급한 모듈 재화),
/// first_clear(Game / raid_first_clear, 구역 첫 클리어 확정 — Stage, BonusAdded·BonusTotal = 판매 보너스 비율).
/// </summary>
public sealed class InsightNight
{
    public int S { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int? Day { get; set; }
    public string? Stage { get; set; }
    public double? BonusAdded { get; set; }
    public double? BonusTotal { get; set; }
    public double? Credits { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    internal InsightNight ForSession(int s)
    {
        InsightNight copy = (InsightNight)MemberwiseClone();
        copy.S = s;
        return copy;
    }
}

/// <summary>
/// 엔딩 관련 이벤트 하나. Kind = retire(Game / retire_completed), retire_failed, choice(ending_choice),
/// chest_scanned·chest_hit·chest_opened(NightRaid / end_chest_*). Play = 그 세이브의 누적 플레이 시간(이 버전의 세션만, 일시정지 제외, 초).
/// 엔드 상자의 Gravity·Homing은 스캔이면 요구 수치, 명중이면 맞은 탄의 수치이고 Raid는 습격 시작 후 게임 초다.
/// </summary>
public sealed class InsightEnding
{
    public int S { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int? Day { get; set; }
    public double Play { get; set; }
    public string? Type { get; set; }
    public string? Choice { get; set; }
    public double? Panel { get; set; }
    public bool? First { get; set; }
    public string? Reason { get; set; }
    public double? Balance { get; set; }
    public double? BalanceAfter { get; set; }
    public double? Loans { get; set; }
    public double? Credits { get; set; }
    public bool? Endless { get; set; }
    public string? Stage { get; set; }
    public double? Raid { get; set; }
    public double? Gravity { get; set; }
    public double? Homing { get; set; }
    public bool? Scanned { get; set; }
    public double? Absorb { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    /// <summary>플레이어 번호와 누적 플레이 시간을 붙인 얕은 복사본. 캐시된 세션 기록은 바꾸지 않는다.</summary>
    internal InsightEnding ForSession(int s, double play)
    {
        InsightEnding copy = (InsightEnding)MemberwiseClone();
        copy.S = s;
        copy.Play = play;
        return copy;
    }
}

/// <summary>
/// 슬롯머신 회전 하나(Shop / slot_machine_spin_started, 결제 성공 때 기록). S = 플레이어, Session = 이 버전 안의 세션 번호.
/// Prize는 회전 시작 때 정해진 지급 예정 금액(plannedPrize)이며, 회전 중 오브젝트가 꺼지면 실제로는 지급되지 않을 수 있다.
/// Pool = oddsPool(first_three, last_funds, ordinary), Symbol = 당첨 심볼 이름(꽝이면 none).
/// </summary>
public sealed class InsightSpin
{
    public int S { get; set; }
    public int Session { get; set; }
    public int? Day { get; set; }
    public bool Night { get; set; }
    public string Pool { get; set; } = string.Empty;
    public double? Before { get; set; }
    public double Price { get; set; }
    public double Prize { get; set; }
    public string Symbol { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    /// <summary>플레이어·세션 번호를 붙인 얕은 복사본. 캐시된 세션 기록은 바꾸지 않는다.</summary>
    internal InsightSpin ForSession(int s, int session)
    {
        InsightSpin copy = (InsightSpin)MemberwiseClone();
        copy.S = s;
        copy.Session = session;
        return copy;
    }
}

/// <summary>
/// 튜토리얼 sequence 한 번의 진행. Outcome은 tutorial_ended의 reason(completed, cancelled, abandoned, error, debug_ended),
/// 건너뛰기면 skipped, 끝 기록 없이 끝났으면 빈 문자열(앱 종료 — 마지막 시작 단계에서 멈춤)이다. Debug면 통계에서 뺀다.
/// </summary>
public sealed class InsightTutorial
{
    public int S { get; set; }
    public string Sequence { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public bool Debug { get; set; }
    public List<InsightTutorialStep> Steps { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    /// <summary>플레이어 번호를 붙인 복사본(단계 목록도 복사). 캐시된 세션 기록은 바꾸지 않는다.</summary>
    internal InsightTutorial ForSession(int s)
    {
        InsightTutorial copy = (InsightTutorial)MemberwiseClone();
        copy.S = s;
        copy.Steps = Steps.Select(x => x.Copy()).ToList();
        return copy;
    }

    /// <summary>다음 세션에서 이어진 같은 진행을 합친다.</summary>
    internal void Absorb(InsightTutorial next)
    {
        foreach (InsightTutorialStep step in next.Steps)
        {
            InsightTutorialStep? same = Steps.Find(x => x.Index == step.Index && x.Id == step.Id);
            if (same == null)
            {
                Steps.Add(step.Copy());
                continue;
            }

            same.Completed |= step.Completed;
            same.Duration = step.Duration ?? same.Duration;
        }

        Outcome = next.Outcome;
        Debug |= next.Debug;
    }
}

/// <summary>튜토리얼 단계 하나. 시작 기록이 있으면 목록에 있고, Duration은 step_completed의 duration(실제 초)이다.</summary>
public sealed class InsightTutorialStep
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public double? Duration { get; set; }

    internal InsightTutorialStep Copy() => (InsightTutorialStep)MemberwiseClone();
}

/// <summary>
/// 플레이어(세션)의 n일차 낮 또는 밤 한 구간. Seconds는 일시정지를 뺀 실제 시간, RaidSeconds는 밤 중 습격 시작→종료 합이다.
/// Ended면 세션이 이 구간에서 끝났다(이탈). LastRaid는 밤의 마지막 습격 결과(success, fail, 비면 모름)이고
/// CleanEnd는 세션이 정상 종료됐는지(요약의 endedCleanly)다.
/// </summary>
public sealed class InsightPhase
{
    public int S { get; set; }
    public int Day { get; set; }
    public bool Night { get; set; }
    public double Seconds { get; set; }
    public double? RaidSeconds { get; set; }
    public bool Ended { get; set; }
    public string LastRaid { get; set; } = string.Empty;
    public bool CleanEnd { get; set; }

    /// <summary>이 구간에서 파산(Game / bankrupt)했으면 true.</summary>
    public bool Bankrupt { get; set; }

    /// <summary>잔액. 낮 = 영업 시작(shop_opened) 때, 밤 = 그날 수동 영업 종료(shop_close_requested) 때. 기록이 없으면 null.</summary>
    public double? Balance { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    /// <summary>세션 번호와 정상 종료 여부를 붙인 얕은 복사본. 캐시된 세션 기록은 바꾸지 않는다.</summary>
    internal InsightPhase ForSession(int s, bool cleanEnd)
    {
        InsightPhase copy = (InsightPhase)MemberwiseClone();
        copy.S = s;
        copy.CleanEnd = cleanEnd;
        return copy;
    }
}

/// <summary>플레이어(세션) 하루의 상점 기록. Builds는 그날 작업대 퇴장 때 구성이 바뀐 총마다 파츠 이름별 개수다.</summary>
public sealed class InsightDay
{
    public int S { get; set; }
    public int Day { get; set; }
    public int Sales { get; set; }
    public double SalesSum { get; set; }
    public int Priced { get; set; }
    public int Satisfied { get; set; }
    public List<Dictionary<string, int>> Builds { get; set; } = new();

    /// <summary>Builds와 같은 순서로, 그 빌드를 만든 주문의 손님 요청 ID(order_created의 requestIds). 주문을 모르면 빈 배열.</summary>
    public List<string[]> BuildRequests { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    /// <summary>세션 번호를 붙인 얕은 복사본. 캐시된 세션 기록은 바꾸지 않는다.</summary>
    internal InsightDay ForSession(int s)
    {
        InsightDay copy = (InsightDay)MemberwiseClone();
        copy.S = s;
        return copy;
    }
}

/// <summary>주문 하나. Builds는 수정 횟수(구성이 바뀐 작업대 퇴장), BuildsAfterTest는 그중 첫 시험 사격 입력 뒤의 횟수다.</summary>
public sealed class InsightOrder
{
    public int S { get; set; }
    public int? Day { get; set; }
    public string Npc { get; set; } = string.Empty;
    public int Builds { get; set; }
    public int BuildsAfterTest { get; set; }
    public int Tests { get; set; }
    public double? WorkbenchSeconds { get; set; }
    public bool Sold { get; set; }
    public double? Price { get; set; }
    public bool? Satisfied { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    internal string OrderId { get; set; } = string.Empty;

    /// <summary>손님 요청 ID(order_created·order_restored의 requestIds). 예: tag/gravity-gun, module/penetration/min-1.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal string[] Requests { get; set; } = Array.Empty<string>();

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    /// <summary>다른 세션에 다시 기록된 같은 주문(저장 불러오기의 order_restored 등)을 합친다.</summary>
    internal void Merge(InsightOrder other)
    {
        Npc = Npc.Length > 0 ? Npc : other.Npc;
        Day = Day.HasValue && other.Day.HasValue ? Math.Min(Day.Value, other.Day.Value) : Day ?? other.Day;
        Builds += other.Builds;
        BuildsAfterTest += other.BuildsAfterTest;
        Tests += other.Tests;
        WorkbenchSeconds = WorkbenchSeconds.HasValue || other.WorkbenchSeconds.HasValue
            ? (WorkbenchSeconds ?? 0) + (other.WorkbenchSeconds ?? 0)
            : null;
        Sold |= other.Sold;
        Price ??= other.Price;
        Satisfied ??= other.Satisfied;
    }

    /// <summary>세션 번호를 붙인 얕은 복사본. 캐시된 세션 기록은 바꾸지 않는다.</summary>
    internal InsightOrder ForSession(int s)
    {
        InsightOrder copy = (InsightOrder)MemberwiseClone();
        copy.S = s;
        return copy;
    }

    internal void SetNpc(string profileId)
    {
        if (profileId.Length > 0)
        {
            Npc = CustomerNames.Name(profileId);
        }
    }
}

/// <summary>
/// 습격 하나. Parts는 습격 총의 파츠 이름별 개수(Gun = loadout: 습격 시작 코드, fired: 가장 많이 쏜 구성, 없으면 null)다.
/// ResponseMean은 적 발견→처치 시간(게임 초)의 평균이고, Retried는 실패 뒤 같은 세션에서 다시 습격했는지다.
/// </summary>
public sealed class InsightRaid
{
    public int S { get; set; }
    public int? Day { get; set; }
    public string Stage { get; set; } = string.Empty;
    public bool? Survived { get; set; }
    public double? Credits { get; set; }
    public string Gun { get; set; } = string.Empty;
    public Dictionary<string, int>? Parts { get; set; }
    public double? ResponseMean { get; set; }
    public int ResponseCount { get; set; }
    public bool? Retried { get; set; }
    public bool? RetriedSameStage { get; set; }

    /// <summary>습격 시작에 기록된 총의 관통·폭발 수치(총구 중 최댓값, 방사 절반 반영). 이후 빌드부터.</summary>
    public double? Penetration { get; set; }
    public double? Explosion { get; set; }

    /// <summary>습격 결과 로그의 duration(습격 시작 후 게임 초)과 enemyKills.</summary>
    public double? Duration { get; set; }
    public double? Kills { get; set; }

    /// <summary>습격 시작의 입장 비용(entryCost)과 연습 습격 여부(training). 이후 빌드부터.</summary>
    public double? EntryCost { get; set; }
    public bool? Training { get; set; }

    /// <summary>습격 시작 때 만든 모듈·재료 상자 수, 이미 해금해 재료 상자로 바뀐 모듈 자리 수, 재료 상자 하나의 모듈 재화.</summary>
    public double? SpawnedModule { get; set; }
    public double? SpawnedMaterial { get; set; }
    public double? ReplacedModule { get; set; }
    public double? MaterialPerCrate { get; set; }

    /// <summary>습격 종료의 dollarReward(지갑에 들어간 달러, 탈출 성공 때만 0 초과)와 rewardsApplied(획득물이 공방에 남는지).</summary>
    public double? Dollar { get; set; }
    public bool? Applied { get; set; }

    /// <summary>습격 종료의 materialCrateCredits(주운 재료 재화 합)와 materialPickups(재료 픽업 수). 반영 여부와 관계없이 남는다.</summary>
    public double? Material { get; set; }
    public double? Pickups { get; set; }

    /// <summary>습격 종료의 exitReason(extracted, died, ending_quit = 엔드 상자 END 화면에서 그만하기). 이전 빌드는 빈 문자열.</summary>
    public string Exit { get; set; } = string.Empty;

    /// <summary>습격 종료의 moduleCrateRewards(그 습격에서 모듈 상자로 얻은 모듈 ID). 종료 기록이 없으면 null.</summary>
    public List<int>? Rewards { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    internal string SaveId { get; set; } = string.Empty;

    /// <summary>세션 번호를 붙인 얕은 복사본. 캐시된 세션 기록은 바꾸지 않는다.</summary>
    internal InsightRaid ForSession(int s)
    {
        InsightRaid copy = (InsightRaid)MemberwiseClone();
        copy.S = s;
        return copy;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    internal Dictionary<string, int> Shots { get; } = new(StringComparer.Ordinal);
}
