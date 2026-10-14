namespace LogServer;

/// <summary>
/// 업로드 봉투. Unity JsonUtility로 직렬화된 UploadBatch와 필드명이 일치하는 요청 모델이다.
/// sessionId와 로그 항목 배열 entries를 담으며 서버는 이 객체를 읽어 세션 CSV와 요약을 작성한다.
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

    /// <summary>세션 전체 로그 항목. 상한은 클라이언트에서 20,000건으로 제한한다.</summary>
    public LogEntry[] Entries { get; set; } = System.Array.Empty<LogEntry>();
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
