namespace LogServer;

/// <summary>
/// 분석 서버가 로그를 복사해 가져갈 때 쓰는 내보내기 API.
/// 세션 목록(변경 시각 포함)과 세션별 통합 CSV·메타·지표 파일을 내준다. 모두 X-Api-Key 인증을 거친다.
/// 분석 서버는 목록의 updatedUtc가 자기 사본과 다른 세션만 내려받으므로, 수신 서버는 상태를 기억하지 않는다.
/// </summary>
public static class ExportEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>내보내기 엔드포인트를 앱에 연결한다.</summary>
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/export/index", HandleIndex);
        app.MapGet("/api/export/sessions/{sessionId}/csv", HandleCsv);
        app.MapGet("/api/export/sessions/{sessionId}/meta", HandleMeta);
        app.MapGet("/api/export/sessions/{sessionId}/metrics", HandleMetrics);
    }

    /// <summary>
    /// GET /api/export/index 처리기. 저장된 모든 세션의 ID와 마지막 변경 시각(UTC, ISO 8601)을 돌려준다.
    /// 입력값은 없으며 200과 {generatedUtc, sessions:[{sessionId, updatedUtc}]} JSON을 반환한다.
    /// 세션 파일이 하나도 없는 항목은 목록에서 빠진다.
    /// </summary>
    private static IResult HandleIndex(CsvSessionStore store)
    {
        List<ExportIndexItem> sessions = new();
        foreach ((string sessionId, DateTime updated) in store.ListSessionChanges())
        {
            sessions.Add(new ExportIndexItem
            {
                SessionId = sessionId,
                UpdatedUtc = DateTime.SpecifyKind(updated, DateTimeKind.Utc).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        return Results.Json(new ExportIndexResponse
        {
            GeneratedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            Sessions = sessions,
        }, _jsonOptions);
    }

    /// <summary>
    /// GET /api/export/sessions/{sessionId}/csv 처리기. 세션의 통합 CSV를 스트림으로 내려준다.
    /// sessionId를 입력으로 사용하며 형식이 틀리면 400, 파일이 없으면 404를 반환한다.
    /// </summary>
    private static IResult HandleCsv(string sessionId, CsvSessionStore store)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        string path = store.SessionCsvPath(sessionId);
        return File.Exists(path)
            ? Results.Stream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16),
                "text/csv; charset=utf-8", sessionId + ".csv")
            : Results.NotFound();
    }

    /// <summary>
    /// GET /api/export/sessions/{sessionId}/meta 처리기. 세션 메타 JSON(앱 버전·플랫폼·시작/종료 시각)을 내려준다.
    /// sessionId를 입력으로 사용하며 형식이 틀리면 400, 파일이 없으면 404를 반환한다.
    /// </summary>
    private static IResult HandleMeta(string sessionId, CsvSessionStore store)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        string path = store.MetaPath(sessionId);
        return File.Exists(path)
            ? Results.File(path, "application/json; charset=utf-8", sessionId + ".meta.json")
            : Results.NotFound();
    }

    /// <summary>
    /// GET /api/export/sessions/{sessionId}/metrics 처리기. 세션 지표 CSV(Count·Measure 누적)를 내려준다.
    /// sessionId를 입력으로 사용하며 형식이 틀리면 400, 파일이 없으면 404를 반환한다.
    /// </summary>
    private static IResult HandleMetrics(string sessionId, CsvSessionStore store)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        string path = store.MetricsCsvPath(sessionId);
        return File.Exists(path)
            ? Results.File(path, "text/csv; charset=utf-8", sessionId + ".metrics.csv")
            : Results.NotFound();
    }
}
