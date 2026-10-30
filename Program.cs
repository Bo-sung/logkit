using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogServer;

/// <summary>
/// GGGG 분석 서버 진입점. 수신 서버에서 세션 로그를 주기적으로 복사해 미러에 쌓고,
/// 그 사본으로 버전 통계·가설 통계·총기 통계·습격 리플레이·맵 이미지를 제공한다.
/// 수신 서버와는 HTTP로만 이야기하므로 두 서버는 따로 배포·재시작할 수 있다.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex _monthPattern = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// 분석 서버를 기동한다. 게임 규약 JSON과 카탈로그를 읽고, 미러 동기화 서비스와 분석 API·페이지를 연결한 뒤 요청을 처리한다.
    /// args를 받아들이며, 미러 동기화는 백그라운드에서 주기적으로 돌고 기동 시에는 로컬 미러로 바로 응답할 수 있다.
    /// </summary>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        AnalyticsOptions options = builder.Configuration.Get<AnalyticsOptions>() ?? new AnalyticsOptions();
        WeaponCode.HmacKey = options.WeaponCodeHmacKey;
        GameConfig.Load(builder.Environment.ContentRootPath);
        ModuleCatalog.Load(Path.Combine(builder.Environment.ContentRootPath, GameConfig.ConfigDirectoryName, "module-catalog.json"));
        CustomerCatalog.Load(Path.Combine(builder.Environment.ContentRootPath, GameConfig.ConfigDirectoryName, "customer-catalog.json"));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<StorageOptions>(options);
        builder.Services.AddSingleton<CsvSessionStore>();
        builder.Services.AddSingleton<SessionSummary>();
        builder.Services.AddSingleton<MapStore>();
        builder.Services.AddSingleton<GameAnalytics>();
        builder.Services.AddSingleton<IngestMirror>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestMirror>());
        // 통계 JSON(수백 KB)과 세션 CSV 내려받기를 압축해 원격 브라우저의 전송 시간을 줄인다
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
            // 맵 이미지 업로드(20MB)가 들어가도록 본문 상한을 맞춘다
            kestrel.Limits.MaxRequestBodySize = 25L * 1024 * 1024;
        });

        WebApplication app = builder.Build();

        app.UseResponseCompression();
        app.UseMiddleware<ApiKeyAuth>(options.ApiKey);
        app.MapGet("/", () => Results.Redirect("/analytics"));
        app.MapGet("/health", () => Results.Text("ok", "text/plain"));
        app.MapGet("/api/mirror/status", (IngestMirror mirror) => Results.Json(mirror.Status, _jsonOptions));
        app.MapGet("/api/months", HandleMonths);
        app.MapGet("/analytics", (AnalyticsOptions o) => Results.Content(PageHtml(AnalyticsPage.Html, o), "text/html; charset=utf-8"));
        app.MapGet("/weapons", (AnalyticsOptions o) => Results.Content(PageHtml(WeaponsPage.Html, o), "text/html; charset=utf-8"));
        app.MapGet("/insights", (AnalyticsOptions o) => Results.Content(PageHtml(InsightsPage.Html, o), "text/html; charset=utf-8"));
        app.MapGet("/replay", (AnalyticsOptions o) => Results.Content(PageHtml(ReplayPage.Html, o), "text/html; charset=utf-8"));
        app.MapGet("/api/analytics", HandleAnalytics);
        app.MapGet("/api/analytics/weapons", HandleWeaponAnalytics);
        app.MapGet("/api/analytics/insights", HandleInsights);
        app.MapGet("/api/analytics/replay", HandleReplay);
        app.MapGet("/api/maps", (MapStore maps) => Results.Json(new { maps = maps.List() }, _jsonOptions));
        app.MapGet("/api/maps/{scene}/image", HandleMapImage);
        app.MapPost("/api/maps/{scene}", HandleMapUpload);
        app.MapDelete("/api/maps/{scene}", (string scene, MapStore maps) =>
            !MapStore.IsValidScene(scene) ? Results.BadRequest(new { error = "invalid scene" })
            : maps.Delete(scene) ? Results.Json(new { scene, deleted = true }, _jsonOptions) : Results.NotFound());
        app.MapGet("/report/full", HandleFullReport);

        app.Services.GetRequiredService<CsvSessionStore>().EnsureDirectories();

        // 기동 직후 로컬 미러의 세션 요약을 미리 만들어 첫 통계 페이지가 CSV 전체 파싱을 기다리지 않게 한다
        app.Lifetime.ApplicationStarted.Register(() => Task.Run(() =>
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                app.Services.GetRequiredService<GameAnalytics>().Warm();
                app.Logger.LogInformation("세션 요약 예열 완료 ({Seconds:F1}초)", watch.Elapsed.TotalSeconds);
            }
            catch (Exception e)
            {
                app.Logger.LogWarning(e, "세션 요약 예열 실패");
            }
        }));
        app.Run();
    }

    /// <summary>
    /// GET /api/analytics 처리기. 게임 이벤트 로그를 버전별로 집계한 플레이 분석을 돌려준다.
    /// month(yyyy-MM, 생략 시 전체 월)와 excludeEditor(true면 에디터 세션 제외)를 사용하며
    /// month 형식이 틀리면 400, 그 외에는 200과 분석 JSON을 반환한다.
    /// </summary>
    private static IResult HandleAnalytics(string? month, string? excludeEditor, GameAnalytics analytics)
    {
        if (!string.IsNullOrEmpty(month) && !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        bool skipEditor = string.Equals(excludeEditor, "true", StringComparison.OrdinalIgnoreCase);
        return Results.Json(analytics.Build(month, skipEditor), _jsonOptions);
    }

    /// <summary>
    /// GET /api/analytics/weapons 처리기. 작업대 퇴장·시험 사격 입력의 총기 코드를 버전별로 집계해 돌려준다.
    /// month(yyyy-MM, 생략 시 전체 월)와 excludeEditor(true면 에디터 세션 제외)를 사용하며
    /// month 형식이 틀리면 400, 그 외에는 200과 총기 집계 JSON을 반환한다.
    /// </summary>
    private static IResult HandleWeaponAnalytics(string? month, string? excludeEditor, GameAnalytics analytics)
    {
        if (!string.IsNullOrEmpty(month) && !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        bool skipEditor = string.Equals(excludeEditor, "true", StringComparison.OrdinalIgnoreCase);
        return Results.Json(analytics.BuildWeapons(month, skipEditor), _jsonOptions);
    }

    /// <summary>
    /// GET /api/analytics/insights 처리기. 일차·주문·습격 단위 세션 기록을 버전별로 모아 돌려준다.
    /// month(yyyy-MM, 생략 시 전체 월)와 excludeEditor(true면 에디터 세션 제외)를 사용하며
    /// month 형식이 틀리면 400, 그 외에는 200과 기록 JSON을 반환한다.
    /// </summary>
    /// <summary>
    /// GET /api/analytics/replay 처리기. month·excludeEditor는 다른 통계와 같고, version·stage로 리플레이할 습격을 고른다.
    /// month 형식이 틀리면 400, 그 외에는 200과 궤적 JSON을 반환한다.
    /// </summary>
    private static IResult HandleReplay(string? month, string? excludeEditor, string? version, string? stage, GameAnalytics analytics)
    {
        if (!string.IsNullOrEmpty(month) && !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        bool skipEditor = string.Equals(excludeEditor, "true", StringComparison.OrdinalIgnoreCase);
        return Results.Json(analytics.BuildReplay(month, skipEditor, version, stage), _jsonOptions);
    }

    /// <summary>GET /api/maps/{scene}/image 처리기. 맵 윗면 이미지를 돌려준다(갱신 시각을 ETag로 써서 브라우저 캐시).</summary>
    private static IResult HandleMapImage(string scene, MapStore maps, HttpContext context)
    {
        if (!MapStore.IsValidScene(scene) || maps.Read(scene) is not { } info || maps.ImagePath(scene) is not { } path)
        {
            return Results.NotFound();
        }

        string etag = "\"" + info.UpdatedUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";
        context.Response.Headers.CacheControl = "private, max-age=300";
        return Results.File(path, info.Type, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(etag));
    }

    /// <summary>
    /// POST /api/maps/{scene} 처리기. multipart 폼의 image(PNG·JPEG·WebP)와 좌표 범위로 맵 이미지를 등록·교체한다.
    /// 범위는 minX·maxX·minZ·maxZ 필드나, 같은 키를 가진 bounds JSON 파일(Unity 캡처 도구가 만드는 파일) 중 하나로 받는다.
    /// </summary>
    private static async Task<IResult> HandleMapUpload(string scene, HttpRequest request, MapStore maps)
    {
        if (!MapStore.IsValidScene(scene))
        {
            return Results.BadRequest(new { error = "씬 이름은 영문·숫자·_·- 80자 이내" });
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "multipart/form-data로 보내세요" });
        }

        IFormCollection form = await request.ReadFormAsync();
        IFormFile? image = form.Files.GetFile("image");
        if (image == null || image.Length == 0 || image.Length > MapStore.MaxImageBytes)
        {
            return Results.BadRequest(new { error = "image 파일이 없거나 20MB를 넘습니다" });
        }

        double minX = MapStore.ParseNumber(form["minX"]), maxX = MapStore.ParseNumber(form["maxX"]);
        double minZ = MapStore.ParseNumber(form["minZ"]), maxZ = MapStore.ParseNumber(form["maxZ"]);
        string source = "upload";
        if (form.Files.GetFile("bounds") is { Length: > 0 and < 65536 } boundsFile)
        {
            try
            {
                using StreamReader reader = new(boundsFile.OpenReadStream());
                using JsonDocument doc = JsonDocument.Parse(await reader.ReadToEndAsync());
                double Get(string name) => doc.RootElement.TryGetProperty(name, out JsonElement v) && v.TryGetDouble(out double d) ? d : double.NaN;
                (minX, maxX, minZ, maxZ) = (Get("minX"), Get("maxX"), Get("minZ"), Get("maxZ"));
                source = doc.RootElement.TryGetProperty("source", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "bounds" : "bounds";
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "bounds JSON을 읽을 수 없습니다" });
            }
        }

        byte[] bytes;
        using (MemoryStream buffer = new())
        {
            await image.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }

        string? error = maps.Save(scene, bytes, minX, maxX, minZ, maxZ, source);
        return error != null ? Results.BadRequest(new { error }) : Results.Json(maps.Read(scene), _jsonOptions);
    }

    private static IResult HandleInsights(string? month, string? excludeEditor, GameAnalytics analytics)
    {
        if (!string.IsNullOrEmpty(month) && !_monthPattern.IsMatch(month))
        {
            return Results.BadRequest(new { error = "month must be yyyy-MM" });
        }

        bool skipEditor = string.Equals(excludeEditor, "true", StringComparison.OrdinalIgnoreCase);
        return Results.Json(analytics.BuildInsights(month, skipEditor), _jsonOptions);
    }

    /// <summary>
    /// 페이지 머리글의 GM 툴 링크(/dashboard)를 설정한 GM 툴 주소로 바꾼다.
    /// 분석 서버는 GM 툴과 다른 주소에서 뜨므로, 이 치환 없이는 링크가 분석 서버의 없는 경로를 가리킨다.
    /// html과 options를 입력으로 사용하며 치환된 HTML을 반환한다.
    /// </summary>
    private static string PageHtml(string html, AnalyticsOptions options)
    {
        return html.Replace("href=\"/dashboard\"", "href=\"" + options.GmToolUrl + "\"", StringComparison.Ordinal);
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
    /// GET /report/full — 4개 페이지를 탭으로 묶은 통합 리포트 HTML을 첨부로 내려준다.
    /// ApiKeyAuth 기존 방식(헤더 또는 key 쿼리)으로 검증한다.
    /// </summary>
    private static IResult HandleFullReport(
        string month, HttpContext context, AnalyticsOptions options, GameAnalytics analytics)
    {
        string? provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(provided)) provided = context.Request.Query["key"].FirstOrDefault();
        if (string.IsNullOrEmpty(options.ApiKey) || !string.Equals(provided, options.ApiKey, StringComparison.Ordinal))
            return Results.StatusCode(StatusCodes.Status401Unauthorized);

        string m = string.IsNullOrEmpty(month) || month == "all" ? "all" : month;
        bool skipEditor = false;
        AnalyticsResponse analyticsData = analytics.Build(m, skipEditor);
        WeaponAnalyticsResponse weaponsData = analytics.BuildWeapons(m, skipEditor);
        InsightsResponse insightsData = analytics.BuildInsights(m, skipEditor);
        ReplayResponse replayData = analytics.BuildReplay(m, skipEditor, null, null);

        string savedAt = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        string fileName = $"logkit-report-{m}-{savedAt}.html";
        var pages = new (string page, string label, string html, object data)[]
        {
            ("/analytics", "버전 통계", PageHtml(AnalyticsPage.Html, options), analyticsData),
            ("/weapons", "총기 통계", PageHtml(WeaponsPage.Html, options), weaponsData),
            ("/insights", "가설 통계", PageHtml(InsightsPage.Html, options), insightsData),
            ("/replay", "습격 리플레이", PageHtml(ReplayPage.Html, options), replayData),
        };
        string html = BuildCombinedReportHtml(m, savedAt, pages);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(html);
        return Results.File(bytes, "text/html; charset=utf-8", fileName);
    }

    private static string BuildCombinedReportHtml(string month, string savedAt,
        (string page, string label, string html, object data)[] pages)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><title>GGGG 통계 리포트</title>");
        sb.Append("<style>.tab{display:none}iframe.tab{display:block;width:100%;height:80vh;border:0}</style></head><body>");
        sb.Append("<div class=\"tabs\">");
        foreach (var p in pages)
            sb.Append("<button type=\"button\" class=\"tabbtn\">").Append(p.label).Append("</button>");
        sb.Append("</div>");
        foreach (var p in pages)
        {
            string embedded = CombinedReport.EmbedPage(p.html, p.page, p.data, month, savedAt);
            sb.Append("<iframe class=\"tab\" srcdoc=\"").Append(embedded.Replace("\"", "&quot;")).Append("\"></iframe>");
        }
        sb.Append("<script>");
        sb.Append("document.querySelectorAll('.tabbtn').forEach((b,i)=>b.onclick=()=>document.querySelectorAll('iframe.tab').forEach((f,j)=>f.style.display=i===j?'block':'none'));");
        sb.Append("</script></body></html>");
        return sb.ToString();
    }
}
