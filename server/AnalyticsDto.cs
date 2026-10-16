namespace LogServer;

/// <summary>GET /api/analytics 응답 본문. 범위 안의 모든 세션 로그를 앱 버전별로 누적 집계한 플레이 통계다.</summary>
public sealed class AnalyticsResponse
{
    /// <summary>조회한 월(yyyy-MM). 빈 문자열이면 전체 월.</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>에디터 세션을 제외했으면 true.</summary>
    public bool ExcludeEditor { get; set; }

    /// <summary>습격 타임라인 구간 길이(게임 시간 초).</summary>
    public int TimelineBinSeconds { get; set; }

    /// <summary>버전 오름차순 집계 목록.</summary>
    public List<VersionAnalytics> Versions { get; set; } = new();
}

/// <summary>
/// 버전 하나의 누적 집계. 결측값은 0으로 바꾸지 않고 null로 둔다.
/// </summary>
public sealed class VersionAnalytics
{
    public string Version { get; set; } = string.Empty;

    // 세션
    public int Sessions { get; set; }
    /// <summary>세션 지속 시간 합계(초). 요약의 시작~종료 기준.</summary>
    public double PlaySeconds { get; set; }
    public int CleanEnds { get; set; }
    public long LogRows { get; set; }
    /// <summary>가장 이른·늦은 세션 시작(UTC, yyyy-MM-dd HH:mm:ss).</summary>
    public string FirstSessionUtc { get; set; } = string.Empty;
    public string LastSessionUtc { get; set; } = string.Empty;

    // 상점 — Shop / 총 판매만 집계
    public int Sales { get; set; }
    public int SessionsWithSales { get; set; }
    /// <summary>price가 기록된 판매 수. 판매액 평균의 분모.</summary>
    public int PricedSales { get; set; }
    public double Revenue { get; set; }
    /// <summary>판매 조건 만족 비율(satisfied=true / satisfied가 기록된 판매). 전체 주문 성공률이 아니다.</summary>
    public double? SatisfiedRate { get; set; }
    /// <summary>판매별 판매액. 분포 차트용.</summary>
    public List<double> Prices { get; set; } = new();
    /// <summary>판매별 tagCount. 분포 차트용.</summary>
    public List<int> TagCounts { get; set; } = new();

    // 주문 — Shop / order_completed (success는 고객 사망이 아닌 성공 완료)
    public int OrdersCompleted { get; set; }
    public int OrdersSucceeded { get; set; }
    public int CustomerDeaths { get; set; }

    // 작업대 — 같은 씬의 시간순 진입→퇴장 쌍
    public int WorkbenchVisits { get; set; }
    public List<double> WorkbenchSeconds { get; set; } = new();
    public int UnmatchedWorkbench { get; set; }

    // 야간 습격
    public int SessionsWithRaids { get; set; }
    /// <summary>전체 스테이지 합계.</summary>
    public RaidStat Raids { get; set; } = new();
    /// <summary>스테이지(습격 씬)별 합계.</summary>
    public List<RaidStat> Stages { get; set; } = new();
    /// <summary>종료 이벤트 moduleCrateRewards의 모듈 보상 ID 빈도. 사망 후 실제 지급 여부는 알 수 없다.</summary>
    public List<KeyCount> ModuleRewards { get; set; } = new();
    public List<TimelinePoint> Timeline { get; set; } = new();
    public List<Heatmap> Heatmaps { get; set; } = new();
}

/// <summary>습격 구간 합계. Scene이 비면 전체 스테이지 합계다.</summary>
public sealed class RaidStat
{
    public string Scene { get; set; } = string.Empty;
    public int Raids { get; set; }
    /// <summary>종료 이벤트가 기록된 습격 수.</summary>
    public int Ended { get; set; }
    /// <summary>습격 시작 없이 시작된 구간 수(기록 누락).</summary>
    public int WithoutStart { get; set; }
    public int Survived { get; set; }
    /// <summary>survived가 기록된 종료 중 생존 비율.</summary>
    public double? SurvivedRate { get; set; }
    /// <summary>종료 이벤트 duration 평균(게임 시간 초).</summary>
    public double? AvgDuration { get; set; }
    public double? AvgEndHealth { get; set; }
    public double? AvgEnemyKills { get; set; }
    public double? AvgModuleCrateCredits { get; set; }
    public double? AvgMaterialCrateCredits { get; set; }
    public int Blocks { get; set; }
    public int EnemiesSpawned { get; set; }
    public int EnemiesSpotted { get; set; }
    public int EnemiesKilled { get; set; }
}

/// <summary>이름-횟수 한 쌍.</summary>
public sealed class KeyCount
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>습격 게임 시간 구간 하나의 평균 상태. 플레이어 상태 data_json.time 기준이다.</summary>
public sealed class TimelinePoint
{
    /// <summary>구간 시작(게임 시간 초).</summary>
    public int Sec { get; set; }
    public double? Health { get; set; }
    public double? ModuleCrates { get; set; }
    public double? MaterialCrates { get; set; }
    public double? Tanks { get; set; }
    public double? Helicopters { get; set; }
    public int Samples { get; set; }
}

/// <summary>스테이지 하나의 위치 표본 밀도 격자. 범위는 전 버전 공통이며 X·Z 동일 축척(정사각형)이다.</summary>
public sealed class Heatmap
{
    public string StageId { get; set; } = string.Empty;
    public double MinX { get; set; }
    public double MaxX { get; set; }
    public double MinZ { get; set; }
    public double MaxZ { get; set; }
    /// <summary>한 변의 칸 수. Cells는 행(z) 우선 Grid×Grid 배열이다.</summary>
    public int Grid { get; set; }
    public int[] Cells { get; set; } = Array.Empty<int>();
}

/// <summary>GET /api/analytics/weapons 응답 본문. 작업대 퇴장·시험 사격 입력의 총기 코드를 앱 버전별로 집계한다.</summary>
public sealed class WeaponAnalyticsResponse
{
    public string Month { get; set; } = string.Empty;
    public bool ExcludeEditor { get; set; }
    public List<WeaponVersionStats> Versions { get; set; } = new();
}

/// <summary>
/// 버전 하나의 총기 집계. 빌드 = 작업대 퇴장 때의 총기 구성이며, 같은 주문에서 구성을 바꾸지 않고 다시 나온 퇴장은 빌드로 세지 않는다.
/// 파츠별·조합별 통계는 Configs(서로 다른 구성별 합계)에서 화면이 계산한다.
/// </summary>
public sealed class WeaponVersionStats
{
    public string Version { get; set; } = string.Empty;
    public int Sessions { get; set; }
    /// <summary>빌드가 1개 이상인 세션 수.</summary>
    public int SessionsWithBuilds { get; set; }
    /// <summary>workbench_exited 전체 수.</summary>
    public int WorkbenchExits { get; set; }
    public int Builds { get; set; }
    /// <summary>같은 주문에서 직전 퇴장과 구성이 같은 퇴장 수(빌드에서 제외).</summary>
    public int RepeatExits { get; set; }
    /// <summary>게임이 코드를 만들지 못한 퇴장(weaponCodeStatus가 ok가 아니거나 코드 없음).</summary>
    public int ExportFailed { get; set; }
    /// <summary>코드가 있으나 서버가 해석하지 못한 퇴장(손상·형식 변경).</summary>
    public int DecodeFailed { get; set; }
    public int CustomNamedBuilds { get; set; }
    /// <summary>색을 지정한 블록이 하나라도 있는 빌드 수.</summary>
    public int ColoredBuilds { get; set; }
    /// <summary>판매 이벤트 전체 수(옛 이름 총 판매 포함).</summary>
    public int Sales { get; set; }
    /// <summary>구성을 아는 판매 수 = SalesWithCode + SalesInferred.</summary>
    public int SalesLinked { get; set; }
    /// <summary>weapon_sold에 실제 판매 총 코드가 있는 판매 수.</summary>
    public int SalesWithCode { get; set; }
    /// <summary>코드가 없어(이전 빌드) 같은 orderId의 마지막 빌드로 추정한 판매 수. 작업대 밖 교체가 있으면 틀릴 수 있다.</summary>
    public int SalesInferred { get; set; }
    /// <summary>attack_input 전체 수(공방 주간의 발사 입력 — 실제 발사 성공이 아님).</summary>
    public int AttackInputs { get; set; }
    public int AttackInputsDecoded { get; set; }

    /// <summary>Shop / weapon_fired 수(공방 주간의 실제 발사 성공). WithoutId는 구성 ID가 없는 발사.</summary>
    public int ShopShots { get; set; }
    public int ShopShotsWithoutId { get; set; }

    /// <summary>습격 구간 수와 그중 습격 시작에 휴대 총 코드가 있는 습격 수.</summary>
    public int Raids { get; set; }
    public int RaidsWithLoadout { get; set; }
    /// <summary>습격 중 처음 쓰는 구성으로 바꾼 기록(raid_weapon_config) 수.</summary>
    public int RaidWeaponChanges { get; set; }
    /// <summary>NightRaid / weapon_fired 수. WithoutId는 구성 ID가 없는 발사.</summary>
    public int RaidShots { get; set; }
    public int RaidShotsWithoutId { get; set; }
    /// <summary>스테이지별 습격 발사 위치 밀도. 범위는 전 버전 공통.</summary>
    public List<Heatmap> ShotHeatmaps { get; set; } = new();

    public List<WeaponConfig> Configs { get; set; } = new();
}

/// <summary>서로 다른 총기 구성 하나의 버전 내 합계. Key는 게임의 구성 ID(weaponAssemblyId, 이름·색 제외)다.</summary>
public sealed class WeaponConfig
{
    public string Key { get; set; } = string.Empty;
    /// <summary>코드를 본 적 있어 파츠를 아는 구성이면 true. 구성 ID만 있는 발사(코드를 못 본 총)는 false이고 Parts가 비어 있다.</summary>
    public bool Known { get; set; }
    /// <summary>튜토리얼(Day0)이 만들어 주는 기본 블럭 5개 구성이면 true.</summary>
    public bool Tutorial { get; set; }
    /// <summary>파츠 표시 이름 → 블록 수. 코어는 포함하지 않는다.</summary>
    public Dictionary<string, int> Parts { get; set; } = new();
    public int Blocks { get; set; }
    public int Builds { get; set; }
    /// <summary>이 구성을 만든 세션 수.</summary>
    public int Sessions { get; set; }
    public int Sold { get; set; }
    /// <summary>price가 기록된 판매의 판매액 합계와 건수.</summary>
    public double PriceSum { get; set; }
    public int Priced { get; set; }
    public int Satisfied { get; set; }
    public int SatisfiedKnown { get; set; }
    public int AttackInputs { get; set; }
    /// <summary>공방 주간 실제 발사 수.</summary>
    public int ShopShots { get; set; }
    /// <summary>이 구성을 들고 시작한 습격 수.</summary>
    public int RaidLoadouts { get; set; }
    /// <summary>습격 중 실제 발사 수와 스테이지별 내역.</summary>
    public int RaidShots { get; set; }
    public Dictionary<string, int> StageShots { get; set; } = new();
}
