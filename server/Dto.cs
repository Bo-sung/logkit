namespace LogServer;

/// <summary>
/// 업로드 봉투. Unity JsonUtility로 직렬화된 UploadBatch와 필드명이 일치하는 요청 모델이다.
/// sessionId와 로그 항목 배열 entries를 담으며, 긴 세션은 partIndex로 나눠 여러 번 전송할 수 있다.
/// </summary>
public sealed class UploadBatch
{
    /// <summary>세션 식별자. GUID 앞 8자리 + 시작 시각 접미.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>세션 시작 UTC 시각(ISO 8601).</summary>
    public string StartedAtUtc { get; set; } = string.Empty;

    /// <summary>세션 종료 UTC 시각(ISO 8601).</summary>
    public string EndedAtUtc { get; set; } = string.Empty;

    /// <summary>앱 버전.</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>유니티 엔진 버전.</summary>
    public string UnityVersion { get; set; } = string.Empty;

    /// <summary>에디터 실행 여부.</summary>
    public bool IsEditor { get; set; }

    /// <summary>실행 플랫폼.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>정상 종료 경로에서 전송되었으면 true.</summary>
    public bool EndedCleanly { get; set; }

    /// <summary>캡처 상한 초과로 로그를 버렸으면 true.</summary>
    public bool Truncated { get; set; }

    /// <summary>0부터 시작하는 배치 파트 번호. 같은 파트 재전송은 교체로 멱등 처리한다.</summary>
    public int PartIndex { get; set; }

    /// <summary>세션 동안 집계한 플레이 지표. 비어 있으면 서버의 기존 지표를 유지한다.</summary>
    public MetricSample[] Metrics { get; set; } = System.Array.Empty<MetricSample>();

    /// <summary>이 파트에 담긴 로그 항목. 한 파트 최대 20,000건.</summary>
    public LogEntry[] Entries { get; set; } = System.Array.Empty<LogEntry>();
}

/// <summary>
/// 플레이 지표 한 샘플. Count·Measure 호출이 누적한 횟수와 합계다.
/// </summary>
public sealed class MetricSample
{
    /// <summary>지표 키. 클라이언트 GameLogger.Count·Measure의 key다.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>누적 횟수.</summary>
    public long Count { get; set; }

    /// <summary>누적 합계. Measure로 기록한 값의 총합이다.</summary>
    public double Sum { get; set; }
}

/// <summary>
/// 세션 메타. 파트가 도착할 때마다 마지막 값으로 갱신되며 요약 재계산 때 세션 헤더로 쓰인다.
/// </summary>
public sealed class SessionMeta
{
    public string SessionId { get; set; } = string.Empty;
    public string StartedAtUtc { get; set; } = string.Empty;
    public string EndedAtUtc { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string UnityVersion { get; set; } = string.Empty;
    public bool IsEditor { get; set; }
    public string Platform { get; set; } = string.Empty;
    public bool EndedCleanly { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>지표 집계 결과 한 행. 횟수와 평균을 담는다.</summary>
public sealed class MetricStat
{
    /// <summary>지표 키.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>누적 횟수.</summary>
    public long Count { get; set; }

    /// <summary>누적 합계.</summary>
    public double Sum { get; set; }

    /// <summary>평균(sum/count). 횟수가 0이면 0.</summary>
    public double Avg { get; set; }
}

/// <summary>
/// 파생 지표 계산 결과. 두 지표의 몫으로 명중률 같은 비율을 담는다.
/// </summary>
public sealed class DerivedMetricValue
{
    /// <summary>파생 지표 이름(예: 공격 명중률).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>분자 지표 키.</summary>
    public string Numerator { get; set; } = string.Empty;

    /// <summary>분모 지표 키.</summary>
    public string Denominator { get; set; } = string.Empty;

    /// <summary>계산값. 분모가 0이면 null.</summary>
    public double? Value { get; set; }

    /// <summary>표기 단위(기본 %).</summary>
    public string Unit { get; set; } = "%";
}

/// <summary>
/// 로그 항목 하나. Unity LogEntry와 필드명이 일치하는 요청 모델이다.
/// timestampUtc와 level, message 등을 담으며 서버에서 길이 절단 후 CSV 한 행이 된다.
/// </summary>
public sealed class LogEntry
{
    /// <summary>기록 시각 UTC(ISO 8601).</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Info / Warning / Error / Assert / Exception.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Unity 자동 캡처 또는 호출자 지정 분류.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>기록 시점 활성 씬 이름.</summary>
    public string Scene { get; set; } = string.Empty;

    /// <summary>로그 본문.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>구조화 부가 데이터(JSON 문자열) 또는 빈 문자열.</summary>
    public string DataJson { get; set; } = string.Empty;

    /// <summary>유니티 제공 스택트레이스 또는 Environment.StackTrace.</summary>
    public string StackTrace { get; set; } = string.Empty;
}

/// <summary>
/// 요약 CSV 한 행을 담은 세션 목록 항목. 조회 API와 대시보드가 월별 세션 목록으로 반환한다.
/// </summary>
public sealed class SessionRow
{
    /// <summary>세션 식별자.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>세션 시작 시각(yyyy-MM-dd HH:mm:ss, UTC).</summary>
    public string DateUtc { get; set; } = string.Empty;

    /// <summary>세션 지속 시간(초).</summary>
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

    /// <summary>에디터 실행 세션이면 true.</summary>
    public bool IsEditor { get; set; }

    /// <summary>실행 플랫폼.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>앱 버전.</summary>
    public string AppVersion { get; set; } = string.Empty;
}

/// <summary>월 하나의 집계. 요약 파일 한 개를 합산한 수치다.</summary>
public sealed class MonthInfo
{
    /// <summary>월 키(yyyy-MM).</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>해당 월의 세션 수.</summary>
    public int Sessions { get; set; }

    /// <summary>전체 로그 행 수 합계.</summary>
    public int Total { get; set; }

    /// <summary>Warning 행 수 합계.</summary>
    public int Warnings { get; set; }

    /// <summary>Error·Assert 행 수 합계.</summary>
    public int Errors { get; set; }

    /// <summary>Exception 행 수 합계.</summary>
    public int Exceptions { get; set; }
}

/// <summary>GET /api/months 응답 본문.</summary>
public sealed class MonthsResponse
{
    /// <summary>최신순 월 집계 목록.</summary>
    public List<MonthInfo> Months { get; set; } = new();
}

/// <summary>GET /api/sessions 응답 본문.</summary>
public sealed class SessionListResponse
{
    /// <summary>조회한 월 키(yyyy-MM).</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>필터를 통과한 세션 수.</summary>
    public int Count { get; set; }

    /// <summary>세션 목록.</summary>
    public List<SessionRow> Sessions { get; set; } = new();
}

/// <summary>GET /api/sessions/{sessionId} 응답 본문. 요약 메타와 필터링한 항목 페이지를 담는다.</summary>
public sealed class SessionDetailResponse
{
    /// <summary>세션 식별자.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>세션 CSV 파일이 존재하면 true.</summary>
    public bool HasCsv { get; set; }

    /// <summary>요약 행이 존재하면 true.</summary>
    public bool HasSummaryRow { get; set; }

    /// <summary>세션 시작 시각(yyyy-MM-dd HH:mm:ss, UTC).</summary>
    public string DateUtc { get; set; } = string.Empty;

    /// <summary>세션 지속 시간(초).</summary>
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

    /// <summary>에디터 실행 세션이면 true.</summary>
    public bool IsEditor { get; set; }

    /// <summary>실행 플랫폼.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>앱 버전.</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>조건을 통과한 전체 항목 수.</summary>
    public int Matched { get; set; }

    /// <summary>이번 페이지에 담긴 항목 수.</summary>
    public int Returned { get; set; }

    /// <summary>이번 페이지 시작 위치(0부터).</summary>
    public int Offset { get; set; }

    /// <summary>조건을 통과한 항목 페이지.</summary>
    public List<LogEntry> Entries { get; set; } = new();

    /// <summary>세션에 집계된 플레이 지표. 횟수 내림차순.</summary>
    public List<MetricStat> Metrics { get; set; } = new();

    /// <summary>설정된 파생 지표 계산값(명중률 등).</summary>
    public List<DerivedMetricValue> Derived { get; set; } = new();
}

/// <summary>GET /api/metrics 응답 본문. 월 전체 세션을 합산한 지표 집계를 담는다.</summary>
public sealed class MonthMetricsResponse
{
    /// <summary>조회한 월 키(yyyy-MM).</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>합산에 포함된 세션 수.</summary>
    public int Sessions { get; set; }

    /// <summary>월 전체 지표 집계. 횟수 내림차순.</summary>
    public List<MetricStat> Metrics { get; set; } = new();

    /// <summary>설정된 파생 지표 계산값(명중률 등).</summary>
    public List<DerivedMetricValue> Derived { get; set; } = new();
}

/// <summary>DELETE /api/sessions/{sessionId} 응답 본문.</summary>
public sealed class DeleteSessionResponse
{
    /// <summary>삭제한 세션 식별자.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>세션 CSV를 삭제했으면 true.</summary>
    public bool DeletedSessionCsv { get; set; }

    /// <summary>raw gzip을 삭제했으면 true.</summary>
    public bool DeletedRaw { get; set; }

    /// <summary>요약 행을 제거한 월 목록.</summary>
    public List<string> RemovedSummaryMonths { get; set; } = new();
}

/// <summary>DELETE /api/months/{month} 응답 본문.</summary>
public sealed class DeleteMonthResponse
{
    /// <summary>삭제한 월 키(yyyy-MM).</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>세션 파일을 삭제한 세션 수.</summary>
    public int DeletedSessions { get; set; }

    /// <summary>월 요약 파일을 삭제했으면 true.</summary>
    public bool DeletedSummaryFile { get; set; }
}
