namespace LogServer;

/// <summary>
/// 공통 수신 서버(ingest) 설정. appsettings.json에서 바인딩한다.
/// 저장 경로·원문 보존은 StorageOptions를 물려받고, 여기에는 수신·인증·휴지통·대시보드 연결 설정만 둔다.
/// </summary>
public sealed class ServerOptions : StorageOptions
{
    /// <summary>
    /// 관리 키. 삭제·복원 등 데이터를 바꾸는 호출에 쓰며, 역할 키를 비우면 조회·업로드 키로도 쓰인다(예전 단일 키 설정과 같은 동작).
    /// </summary>
    public string ApiKey { get; set; } = "dev-log-key";

    /// <summary>열람 키. GM 툴과 분석 서버(내보내기 호출)가 쓴다. 비우면 ApiKey를 쓴다.</summary>
    public string ReadApiKey { get; set; } = string.Empty;

    /// <summary>업로드 키. 게임 클라이언트(GameLogger)가 POST /api/logs에 쓴다. 비우면 ApiKey를 쓴다.</summary>
    public string UploadApiKey { get; set; } = string.Empty;

    /// <summary>설정된 역할별 키를 인증 미들웨어용 묶음으로 만든다.</summary>
    public ApiKeys ToApiKeys() => new(ApiKey, ReadApiKey, UploadApiKey);

    /// <summary>gzip 수신 본문 상한(바이트). 초과 시 413.</summary>
    public long MaxBodyBytes { get; set; } = 5242880L;

    /// <summary>휴지통 보관 일수. 삭제한 지 이만큼 지난 세션은 매시간 확인해 영구 삭제한다. 0 이하면 자동 삭제하지 않는다.</summary>
    public int TrashRetentionDays { get; set; } = 7;

    /// <summary>수신함 처리 묶음 간격(초). 새 업로드가 오면 이만큼 더 기다렸다가 그사이 도착분까지 한 번에 처리한다.</summary>
    public double InboxProcessIntervalSeconds { get; set; } = 2.0;

    /// <summary>두 지표의 몫으로 계산하는 파생 지표 목록(공격 명중률 등). 비어 있으면 계산하지 않는다.</summary>
    public List<DerivedMetricOptions> DerivedMetrics { get; set; } = new();
}
