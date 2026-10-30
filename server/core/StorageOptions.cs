namespace LogServer;

/// <summary>
/// 세션 저장소 공통 설정. 수신 서버(ingest)와 분석 서버(analytics)가 각자 data 폴더를 가지며 같은 저장 형식을 쓴다.
/// DataPath 아래 sessions·raw·summary 폴더를 만들고, KeepRawJsonl과 RawRetentionDays로 원문 보존을 조절한다.
/// </summary>
public class StorageOptions
{
    /// <summary>세션·요약·raw 저장 루트 디렉터리.</summary>
    public string DataPath { get; set; } = "data";

    /// <summary>수신 원문을 raw 디렉터리에 보존할지 여부. 분석 서버의 미러는 원문이 필요 없어 끈다.</summary>
    public bool KeepRawJsonl { get; set; } = true;

    /// <summary>raw 보존 일수. 기동 시 경과분을 삭제한다.</summary>
    public int RawRetentionDays { get; set; } = 90;
}
