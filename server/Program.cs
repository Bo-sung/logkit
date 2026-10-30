using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogServer;

/// <summary>
/// 공통 로그 수신 서버(ingest) 진입점. 업로드 수신·저장·세션 열람(GM툴)·휴지통·내보내기 API만 담당한다.
/// 게임별 분석(버전 통계·가설 통계·리플레이·맵)은 analytics 폴더의 분석 서버가 이 서버의 내보내기 API를 주기적으로 복사해 처리한다.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex _monthPattern = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// 수신 서버를 기동한다. 설정과 서비스를 등록하고 업로드·조회·삭제·내보내기 엔드포인트를 연결한 뒤 요청을 처리한다.
    /// args를 받아들이며, data 디렉터리를 만들고 만료된 raw 파일을 삭제한 후 현재 스레드에서 계속 대기한다.
    /// </summary>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        ServerOptions options = builder.Configuration.Get<ServerOptions>() ?? new ServerOptions();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<StorageOptions>(options);
        builder.Services.AddSingleton<CsvSessionStore>();
        builder.Services.AddSingleton<SessionSummary>();
        builder.Services.AddSingleton<SessionTrash>();
        builder.Services.AddSingleton<UploadInbox>();
        builder.Services.AddHostedService<InboxProcessor>();
        builder.Services.AddSingleton<SessionEntryCache>();
        // 세션 CSV 내려받기를 압축해 원격 브라우저와 분석 서버의 전송 시간을 줄인다
        builder.Services.AddResponseCompression(compression =>
        {
            compression.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
            compression.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
            compression.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Append("text/csv");
        });
        builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
            brotli => brotli.Level = System.IO.Compression.CompressionLevel.Fastest);
        builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
            gzip => gzip.Level = System.IO.Compression.CompressionLevel.Fastest);
        builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = options.MaxBodyBytes;
        });

        WebApplication app = builder.Build();

        app.UseResponseCompression();
        app.UseMiddleware<ApiKeyAuth>(options.ToApiKeys());
        app.MapGet("/", () => Results.Redirect("/dashboard"));
        app.MapGet("/dashboard", HandleDashboard);
        app.MapPost("/api/logs", HandleUploadAsync);
        app.MapGet("/health", HandleHealth);
        app.MapGet("/api/months", HandleMonths);
        app.MapGet("/api/sessions", HandleSessions);
        app.MapGet("/api/sessions/{sessionId}", HandleSessionDetail);
        app.MapGet("/api/sessions/{sessionId}/csv", HandleSessionCsv);
        app.MapGet("/api/sessions/{sessionId}/raw", HandleSessionRaw);
        app.MapGet("/api/metrics", HandleMonthMetrics);
        app.MapDelete("/api/sessions/{sessionId}", HandleDeleteSession);
        app.MapDelete("/api/months/{month}", HandleDeleteMonth);
        app.MapGet("/api/trash", (ServerOptions o, SessionTrash trash) =>
            Results.Json(new TrashListResponse { RetentionDays = o.TrashRetentionDays, Items = trash.List() }, _jsonOptions));
        app.MapPost("/api/trash/{sessionId}/restore", HandleRestoreTrash);
        app.MapDelete("/api/trash/{sessionId}", (string sessionId, SessionTrash trash) =>
            !CsvSessionStore.IsValidSessionId(sessionId) ? Results.BadRequest(new { error = "invalid sessionId" })
            : trash.Purge(sessionId) ? Results.Json(new { sessionId, purged = true }, _jsonOptions) : Results.NotFound());
        app.MapDelete("/api/trash", (SessionTrash trash) => Results.Json(new { purged = trash.PurgeAll() }, _jsonOptions));
        app.MapGet("/api/summary/{month}.csv", HandleSummary);
        ExportEndpoints.Map(app);

        PrepareStorage(options, app.Services.GetRequiredService<CsvSessionStore>(), app.Logger);

        // 휴지통 자동 비우기: 기동 직후와 매시간, 보관 일수가 지난 항목을 영구 삭제한다
        app.Lifetime.ApplicationStarted.Register(() => Task.Run(async () =>
        {
            SessionTrash trash = app.Services.GetRequiredService<SessionTrash>();
            using PeriodicTimer timer = new(TimeSpan.FromHours(1));
            do
            {
                try
                {
                    int purged = trash.PurgeExpired();
                    if (purged > 0)
                    {
                        app.Logger.LogInformation("휴지통 자동 삭제 {Count}건 (보관 {Days}일)", purged, options.TrashRetentionDays);
                    }
                }
                catch (Exception e)
                {
                    app.Logger.LogWarning(e, "휴지통 자동 삭제 실패");
                }
            }
            while (await timer.WaitForNextTickAsync(app.Lifetime.ApplicationStopping));
        }));
        app.Run();
    }

    /// <summary>
    /// POST /api/logs 처리기. 본문 크기만 검사하고 수신함에 그대로 저장한 뒤 바로 202를 반환한다.
    /// gzip 해제·역직렬화·파트 저장·요약 재계산은 InboxProcessor가 나중에 몰아서 하므로,
    /// 여러 세션이 동시에 자주 보내도 요청 처리는 디스크 쓰기 한 번으로 끝난다.
    /// </summary>
    private static async Task<IResult> HandleUploadAsync(
        HttpContext context,
        ServerOptions options,
        UploadInbox inbox)
    {
        (bool tooLarge, byte[] body) = await ReadBodyAsync(context.Request, options.MaxBodyBytes, context.RequestAborted);
        if (tooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (body.Length == 0)
        {
            return Results.BadRequest(new { error = "empty body" });
        }

        bool bodyIsGzip = string.Equals(context.Request.Headers.ContentEncoding.ToString(), "gzip", StringComparison.OrdinalIgnoreCase);
        await inbox.SaveAsync(body, bodyIsGzip, context.RequestAborted);
        return Results.Json(new { queued = true }, _jsonOptions, statusCode: StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// GET /health 처리기. 서버 생존 확인용 텍스트 응답을 만든다.
    /// 입력값은 없으며 200 상태와 "ok" 본문을 반환한다.
    /// </summary>
    private static IResult HandleHealth()
    {
        return Results.Text("ok", "text/plain");
    }

    /// <summary>
    /// GET /dashboard 처리기. 브라우저 로그 열람 대시보드 HTML을 돌려준다.
    /// 입력값은 없으며 200 상태와 text/html 본문을 반환한다.
    /// </summary>
    private static IResult HandleDashboard()
    {
        return Results.Content(DashboardPage.Html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// GET /api/months 처리기. 요약 파일이 있는 월 목록과 월별 집계를 돌려준다.
    /// 입력값은 없으며 200 상태와 월 목록 JSON을 반환한다.
    /// </summary>
    private static IResult HandleMonths(SessionSummary summary)
    {
        return Results.Json(new MonthsResponse { Months = summary.ListMonths() }, _jsonOptions);
    }

    /// <summary>
    /// GET /api/sessions 처리기. 월별 세션 목록을 검색 조건에 맞춰 돌려준다.
    /// month(필수), version(앱 버전 정확히 일치), q(ID·플랫폼·버전 부분 일치), onlyIssues(경고·오류·예외가 있는 세션만), sort(date·errors·total)를 사용하며
    /// month 형식이 틀리면 400, 그 외에는 200과 세션 목록 JSON을 반환한다.
    /// </summary>
    private static IResult HandleSessions(
        string? month,
        string? version,
        string? q,
        string? onlyIssues,
        string? sort,
        SessionSummary summary)
    {
        if (string.IsNullOrEmpty(month) || !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        List<SessionRow> sessions = summary.GetMonthSessions(month);
        List<string> versions = sessions.Select(row => row.AppVersion).Distinct(StringComparer.Ordinal).ToList();
        versions.Sort((a, b) => AppVersionOrder.Compare(b, a));

        if (!string.IsNullOrEmpty(version))
        {
            sessions = sessions.Where(row => string.Equals(row.AppVersion, version, StringComparison.Ordinal)).ToList();
        }

        if (!string.IsNullOrEmpty(q))
        {
            sessions = sessions.Where(row =>
                row.SessionId.Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.Platform.Contains(q, StringComparison.OrdinalIgnoreCase)
                || row.AppVersion.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (string.Equals(onlyIssues, "true", StringComparison.OrdinalIgnoreCase))
        {
            sessions = sessions.Where(row => row.Warnings + row.Errors + row.Exceptions > 0).ToList();
        }

        sessions = sort switch
        {
            "errors" => sessions.OrderByDescending(row => row.Errors + row.Exceptions).ThenByDescending(row => row.DateUtc).ToList(),
            "total" => sessions.OrderByDescending(row => row.Total).ThenByDescending(row => row.DateUtc).ToList(),
            _ => sessions,
        };

        return Results.Json(new SessionListResponse { Month = month, Count = sessions.Count, Versions = versions, Sessions = sessions }, _jsonOptions);
    }

    /// <summary>
    /// GET /api/sessions/{sessionId} 처리기. 세션 메타와 필터링한 로그 항목 페이지를 돌려준다.
    /// levels(쉼표 구분), category, scene, q(메시지·데이터·스택 부분 일치), offset, limit을 사용하며
    /// 세션 파일과 요약 행이 모두 없으면 404, 그 외에는 200과 상세 JSON을 반환한다.
    /// </summary>
    private static IResult HandleSessionDetail(
        string sessionId,
        string? levels,
        string? category,
        string? scene,
        string? q,
        string? offset,
        string? limit,
        ServerOptions options,
        CsvSessionStore store,
        SessionSummary summary,
        SessionEntryCache entryCache)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        (string Month, SessionRow Row)? meta = summary.FindSession(sessionId);
        List<LogEntry>? parsed = entryCache.Get(sessionId);
        if (meta is null && parsed is null)
        {
            return Results.NotFound();
        }

        SessionRow metaRow = meta?.Row ?? new SessionRow();
        List<LogEntry> entries = parsed ?? new List<LogEntry>();
        HashSet<string> levelSet = ParseLevels(levels);
        int parsedOffset = Math.Max(0, ParseIntQuery(offset));
        int parsedLimit = Math.Clamp(ParseIntQuery(limit, SessionQuery.DefaultLimit), 1, SessionQuery.MaxLimit);
        (int matched, List<LogEntry> page) = SessionQuery.Filter(
            entries, levelSet, category ?? string.Empty, scene ?? string.Empty, q ?? string.Empty, parsedOffset, parsedLimit);

        Dictionary<string, (long Count, double Sum)> metricMap = MetricMath.ToMap(store.ReadMetrics(sessionId));

        return Results.Json(new SessionDetailResponse
        {
            SessionId = sessionId,
            HasCsv = parsed is not null,
            HasSummaryRow = meta is not null,
            DateUtc = metaRow.DateUtc,
            DurationSec = metaRow.DurationSec,
            EndedCleanly = metaRow.EndedCleanly,
            Total = metaRow.Total,
            Warnings = metaRow.Warnings,
            Errors = metaRow.Errors,
            Exceptions = metaRow.Exceptions,
            IsEditor = metaRow.IsEditor,
            Platform = metaRow.Platform,
            AppVersion = metaRow.AppVersion,
            Matched = matched,
            Returned = page.Count,
            Offset = parsedOffset,
            Entries = page,
            Metrics = MetricMath.ToStats(metricMap),
            Derived = MetricMath.Evaluate(options.DerivedMetrics, metricMap),
        }, _jsonOptions);
    }

    /// <summary>
    /// GET /api/sessions/{sessionId}/csv 처리기. 세션 CSV 파일을 내려준다.
    /// sessionId를 입력으로 사용하며 파일이 없으면 404를 반환한다.
    /// </summary>
    private static IResult HandleSessionCsv(string sessionId, CsvSessionStore store)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        // 큰 세션(100MB 이상)도 메모리에 올리지 않고 파일을 그대로 흘려 보낸다
        string path = store.SessionCsvPath(sessionId);
        return File.Exists(path)
            ? Results.Stream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16),
                "text/csv; charset=utf-8", sessionId + ".csv")
            : Results.NotFound();
    }

    /// <summary>
    /// GET /api/sessions/{sessionId}/raw 처리기. 수신 원문 gzip을 내려준다.
    /// sessionId를 입력으로 사용하며 파일이 없으면 404를 반환한다.
    /// </summary>
    private static IResult HandleSessionRaw(string sessionId, CsvSessionStore store)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        byte[]? bytes = store.ReadRawMerged(sessionId);
        return bytes is null
            ? Results.NotFound()
            : Results.File(bytes, "application/gzip", sessionId + ".jsonl.gz");
    }

    /// <summary>
    /// DELETE /api/sessions/{sessionId} 처리기. 세션 파일을 휴지통으로 옮기고 요약 행을 지운다.
    /// sessionId를 입력으로 사용하며 옮긴 것이 없으면 404, 있으면 200과 결과 JSON을 반환한다.
    /// </summary>
    private static IResult HandleDeleteSession(string sessionId, SessionTrash trash)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        if (!trash.MoveToTrash(sessionId))
        {
            return Results.NotFound();
        }

        Console.WriteLine($"trash session={sessionId}");
        return Results.Json(new DeleteSessionResponse { SessionId = sessionId, MovedToTrash = true }, _jsonOptions);
    }

    /// <summary>
    /// POST /api/trash/{sessionId}/restore 처리기. 휴지통의 세션을 되살린다.
    /// 없으면 404, 같은 세션 파일이 이미 있으면 409, 성공하면 200을 반환한다.
    /// </summary>
    private static IResult HandleRestoreTrash(string sessionId, SessionTrash trash)
    {
        if (!CsvSessionStore.IsValidSessionId(sessionId))
        {
            return Results.BadRequest(new { error = "invalid sessionId" });
        }

        return trash.Restore(sessionId) switch
        {
            TrashRestoreResult.Restored => Results.Json(new { sessionId, restored = true }, _jsonOptions),
            TrashRestoreResult.Conflict => Results.Conflict(new { error = "같은 세션 파일이 이미 있어 복원하지 않았습니다" }),
            _ => Results.NotFound(),
        };
    }

    /// <summary>
    /// DELETE /api/months/{month} 처리기. 월의 모든 세션을 휴지통으로 옮기고 월 요약 파일을 지운다.
    /// month(yyyy-MM)를 입력으로 사용하며 지운 것이 없으면 404, 있으면 200과 결과 JSON을 반환한다.
    /// </summary>
    private static IResult HandleDeleteMonth(string month, SessionTrash trash, SessionSummary summary)
    {
        if (string.IsNullOrEmpty(month) || !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        int deletedSessions = 0;
        foreach (SessionRow row in summary.GetMonthSessions(month))
        {
            if (trash.MoveToTrash(row.SessionId))
            {
                deletedSessions++;
            }
        }

        bool deletedSummaryFile = summary.DeleteMonth(month);
        if (deletedSessions == 0 && !deletedSummaryFile)
        {
            return Results.NotFound();
        }

        Console.WriteLine($"trash month={month} sessions={deletedSessions} summaryFile={deletedSummaryFile}");
        return Results.Json(new DeleteMonthResponse
        {
            Month = month,
            DeletedSessions = deletedSessions,
            DeletedSummaryFile = deletedSummaryFile,
        }, _jsonOptions);
    }

    /// <summary>
    /// GET /api/metrics 처리기. 월 전체 세션의 플레이 지표를 합산하고 파생 지표를 계산한다.
    /// month(yyyy-MM)를 입력으로 사용하며, 형식이 틀리면 400, 그 외에는 200과 월 지표 JSON을 반환한다.
    /// </summary>
    private static IResult HandleMonthMetrics(
        string? month,
        ServerOptions options,
        CsvSessionStore store,
        SessionSummary summary)
    {
        if (string.IsNullOrEmpty(month) || !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        List<SessionRow> sessions = summary.GetMonthSessions(month);
        Dictionary<string, (long Count, double Sum)> merged = new(StringComparer.Ordinal);
        foreach (SessionRow session in sessions)
        {
            MetricMath.MergeInto(merged, store.ReadMetrics(session.SessionId));
        }

        return Results.Json(new MonthMetricsResponse
        {
            Month = month,
            Sessions = sessions.Count,
            Metrics = MetricMath.ToStats(merged),
            Derived = MetricMath.Evaluate(options.DerivedMetrics, merged),
        }, _jsonOptions);
    }

    /// <summary>
    /// levels 쿼리 값을 레벨 집합으로 바꾼다.
    /// levels(쉼표 구분)를 입력으로 사용하며 비었거나 null이면 빈 집합(전체 허용)을 반환한다.
    /// </summary>
    private static HashSet<string> ParseLevels(string? levels)
    {
        HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(levels))
        {
            return set;
        }

        foreach (string part in levels.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                set.Add(trimmed);
            }
        }

        return set;
    }

    /// <summary>쿼리 정수를 관대하게 읽는다. text가 없거나 형식이 틀리면 fallback을 반환한다.</summary>
    private static int ParseIntQuery(string? text, int fallback = 0)
    {
        return int.TryParse(text, out int value) ? value : fallback;
    }

    /// <summary>
    /// GET /api/summary/{yyyy-MM}.csv 처리기. 월별 요약 CSV 파일을 내려준다.
    /// month(yyyy-MM)를 입력으로 사용하며 파일이 있으면 text/csv 스트림, 없거나 형식이 틀리면 404를 반환한다.
    /// </summary>
    private static IResult HandleSummary(string month, ServerOptions options)
    {
        if (string.IsNullOrEmpty(month) || !_monthPattern.IsMatch(month))
        {
            return Results.NotFound();
        }

        string path = Path.Combine(options.DataPath, "summary", month + ".csv");
        if (!File.Exists(path))
        {
            return Results.NotFound();
        }

        return Results.File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), "text/csv; charset=utf-8", month + ".csv");
    }

    /// <summary>
    /// 요청 본문을 MaxBodyBytes 이하로 읽는다. Content-Length가 상한을 넘으면 읽지 않고 즉시 거부한다.
    /// request와 maxBytes를 입력으로 사용하며 초과 여부와 읽은 바이트를 반환하고, 초과한 본문은 버린다.
    /// </summary>
    private static async Task<(bool TooLarge, byte[] Body)> ReadBodyAsync(HttpRequest request, long maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength is long declaredLength && declaredLength > maxBytes)
        {
            return (true, System.Array.Empty<byte>());
        }

        using MemoryStream buffer = new();
        byte[] chunk = new byte[64 * 1024];
        long total = 0L;

        while (true)
        {
            int read = await request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return (true, System.Array.Empty<byte>());
            }

            buffer.Write(chunk, 0, read);
        }

        return (false, buffer.ToArray());
    }

    /// <summary>
    /// 기동 시 data 디렉터리를 준비하고 KeepRawJsonl이 켜져 있으면 RawRetentionDays를 넘긴 raw 파일을 지운다.
    /// options와 sessionStore를 사용하며 파일 시스템 상태를 변경하고 결과를 로그로 남긴다.
    /// </summary>
    private static void PrepareStorage(ServerOptions options, CsvSessionStore sessionStore, ILogger logger)
    {
        sessionStore.EnsureDirectories();
        int deleted = sessionStore.DeleteExpiredRawFiles();

        if (string.IsNullOrEmpty(options.ApiKey))
        {
            logger.LogWarning("ApiKey가 비어 있어 /api 경로가 모든 요청을 401으로 거부합니다");
        }
        else if (string.IsNullOrEmpty(options.ReadApiKey) || string.IsNullOrEmpty(options.UploadApiKey))
        {
            logger.LogInformation("읽기·업로드 키가 비어 있어 관리 키로 대신합니다(단일 키 설정)");
        }

        logger.LogInformation(
            "LogServer 준비 완료 dataPath={DataPath} maxBodyBytes={MaxBodyBytes} keepRaw={KeepRawJsonl} expiredRawDeleted={ExpiredRawDeleted}",
            options.DataPath,
            options.MaxBodyBytes,
            options.KeepRawJsonl,
            deleted);
    }
}
