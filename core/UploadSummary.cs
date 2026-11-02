namespace LogServer;

/// <summary>
/// 세션 로그를 정제·집계한 간단 요약. 요약 CSV 한 행과 같은 수치를 담으며 수신함 처리 로그에 쓴다.
/// </summary>
public sealed class UploadSummary
{
    /// <summary>요약 행이 저장된 월(yyyy-MM).</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>요약 기준 시각(세션 시작, 초 단위 표기).</summary>
    public string DateUtc { get; set; } = string.Empty;

    /// <summary>세션 지속 시간(초, 소수 1자리).</summary>
    public double DurationSec { get; set; }

    /// <summary>정상 종료 세션이면 true.</summary>
    public bool EndedCleanly { get; set; }

    /// <summary>전체 로그 행 수.</summary>
    public int Total { get; set; }

    /// <summary>Warning 행 수.</summary>
    public int Warnings { get; set; }

    /// <summary>Error·Assert 행 수.</summary>
    public int Errors { get; set; }

    /// <summary>Exception 행 수.</summary>
    public int Exceptions { get; set; }

    /// <summary>가장 많이 기록된 카테고리. 항목이 없으면 빈 문자열.</summary>
    public string TopCategory { get; set; } = string.Empty;
}
