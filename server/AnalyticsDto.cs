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
