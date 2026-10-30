namespace LogServer;

/// <summary>
/// GGGG 분석 서버 설정. appsettings.json에서 바인딩한다.
/// DataPath는 수신 서버에서 복사해 온 미러 저장소이며, 원문(raw)은 가져오지 않으므로 KeepRawJsonl은 꺼 둔다.
/// </summary>
public sealed class AnalyticsOptions : StorageOptions
{
    /// <summary>분석 서버 자체의 X-Api-Key. 분석 페이지와 /api 호출이 이 값을 요구한다.</summary>
    public string ApiKey { get; set; } = "dev-log-key";

    /// <summary>WeaponCode HMAC 무결성 키 문자열(기본 현재 값 유지로 클라이언트 호환).</summary>
    public string WeaponCodeHmacKey { get; set; } = "WGG.weapon-code.v1.compact-integrity";

    /// <summary>
    /// 페이지 머리글의 GM 툴 링크 주소. 사내 브라우저가 열 수 있는 주소여야 한다(예: http://172.21.10.67:5000/dashboard).
    /// </summary>
    public string GmToolUrl { get; set; } = "http://localhost:5000/dashboard";

    /// <summary>복사해 올 수신 서버 주소(예: http://localhost:5000).</summary>
    public string IngestUrl { get; set; } = "http://localhost:5000";

    /// <summary>수신 서버의 열람 키(ReadApiKey). 세션 목록과 파일을 읽기만 하므로 열람 키면 충분하다.</summary>
    public string IngestApiKey { get; set; } = "dev-log-key";

    /// <summary>수신 서버에서 세션 목록을 다시 확인하는 간격(초).</summary>
    public double SyncIntervalSeconds { get; set; } = 60;

    /// <summary>수신 서버 한 번의 요청 제한 시간(초). 큰 세션 CSV 내려받기를 기준으로 넉넉히 둔다.</summary>
    public double IngestTimeoutSeconds { get; set; } = 120;

    public AnalyticsOptions()
    {
        DataPath = "data";
        KeepRawJsonl = false;
    }
}
