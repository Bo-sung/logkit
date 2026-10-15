namespace LogServer;

/// <summary>GET /api/analytics 응답 본문. 범위 안의 세션을 버전별로 집계한 플레이 분석이다.</summary>
public sealed class AnalyticsResponse
{
    /// <summary>조회한 월(yyyy-MM). 빈 문자열이면 전체 월.</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>에디터 세션을 제외했으면 true.</summary>
    public bool ExcludeEditor { get; set; }

    /// <summary>습격 타임라인 구간 길이(초).</summary>
    public int TimelineBinSeconds { get; set; }

    /// <summary>버전 오름차순 집계 목록.</summary>
    public List<VersionAnalytics> Versions { get; set; } = new();
}

/// <summary>버전 하나의 상점·습격 집계.</summary>
public sealed class VersionAnalytics
{
    public string Version { get; set; } = string.Empty;
    public int Sessions { get; set; }

    // 상점 운영
    /// <summary>상점 시작 횟수(진행한 게임 일수 합계).</summary>
    public int ShopDays { get; set; }
    /// <summary>상점 시작~종료 평균 소요 시간(초).</summary>
    public double? AvgDaySeconds { get; set; }
    /// <summary>하루 평균 금액 증감(종료 금액 - 시작 금액).</summary>
    public double? AvgMoneyGain { get; set; }
    /// <summary>등장한 주문 수(작업대·손님·사격·완료 중 하나라도 기록된 주문).</summary>
    public int Orders { get; set; }
    public int CompletedOrders { get; set; }
    /// <summary>완료 주문 중 성공 비율(0~1).</summary>
    public double? SuccessRate { get; set; }
    /// <summary>완료 주문 중 사망 비율(0~1).</summary>
    public double? DeathRate { get; set; }
    /// <summary>상점 사격 입력 총 횟수.</summary>
    public int Shots { get; set; }
    public double? AvgShotsPerOrder { get; set; }
    /// <summary>주문당 사격 횟수 분포. 인덱스가 사격 횟수이고 마지막 칸은 그 이상.</summary>
    public int[] ShotHistogram { get; set; } = Array.Empty<int>();
    /// <summary>작업대를 쓴 주문의 주문당 작업대 체류 시간 평균(초).</summary>
    public double? AvgWorkbenchSeconds { get; set; }
    public double? AvgCustomerVisits { get; set; }
    public int PresetSaves { get; set; }
    public List<DayMoney> MoneyByDay { get; set; } = new();
    public List<KeyCount> SubmittedGuns { get; set; } = new();
    public List<KeyCount> PresetGuns { get; set; } = new();

    // 야간 습격
    public int RaidRuns { get; set; }
    public int Escapes { get; set; }
    /// <summary>탈출 성공 비율(0~1).</summary>
    public double? EscapeRate { get; set; }
    public double? AvgPlayTime { get; set; }
    public double? AvgVaultReward { get; set; }
    public double? AvgKills { get; set; }
    public double? AvgEscapeHp { get; set; }
    public List<KeyCount> RaidGuns { get; set; } = new();
    public List<KeyCount> RaidModules { get; set; } = new();
    /// <summary>스테이지별 블록 파괴 수.</summary>
    public List<KeyCount> Blocks { get; set; } = new();
    public List<EnemyStat> Enemies { get; set; } = new();
    public List<TimelinePoint> Timeline { get; set; } = new();
    public List<Heatmap> Heatmaps { get; set; } = new();
}

/// <summary>이름-횟수 한 쌍.</summary>
public sealed class KeyCount
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>게임 일수별 상점 종료 시 평균 금액.</summary>
public sealed class DayMoney
{
    public int Day { get; set; }
    public double AvgMoney { get; set; }
    public int Samples { get; set; }
}

/// <summary>적 종류 하나의 생성·발견·처치 합계.</summary>
public sealed class EnemyStat
{
    public string EnemyId { get; set; } = string.Empty;
    public int Spawned { get; set; }
    public int Spotted { get; set; }
    public int Killed { get; set; }
}

/// <summary>습격 경과 시간 구간 하나의 평균 상태.</summary>
public sealed class TimelinePoint
{
    /// <summary>구간 시작(습격 시작 기준 초).</summary>
    public int Sec { get; set; }
    public double? Hp { get; set; }
    public double? Vaults { get; set; }
    public double? Tanks { get; set; }
    public double? Helis { get; set; }
    public int Samples { get; set; }
}

/// <summary>스테이지 하나의 위치 표본 밀도 격자. 범위는 전 버전 공통이다.</summary>
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
