namespace LogServer;

/// <summary>내보내기 목록 한 항목. 분석 서버가 자기 사본과 비교할 세션 ID와 변경 시각.</summary>
public sealed class ExportIndexItem
{
    public string SessionId { get; set; } = string.Empty;
    public string UpdatedUtc { get; set; } = string.Empty;
}

/// <summary>내보내기 목록 응답.</summary>
public sealed class ExportIndexResponse
{
    public string GeneratedUtc { get; set; } = string.Empty;
    public List<ExportIndexItem> Sessions { get; set; } = new();
}
