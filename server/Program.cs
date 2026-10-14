using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LogServer;

/// <summary>
/// appsettings.json의 서버 설정을 담는 옵션 클래스.
/// ApiKey, DataPath, MaxBodyBytes, KeepRawJsonl, RawRetentionDays를 바인딩해 처리기 전체가 참조한다.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>X-Api-Key 헤더와 비교할 키. Unity 클라이언트 기본값과 맞춘 개발용 키는 dev-log-key.</summary>
    public string ApiKey { get; set; } = "dev-log-key";

    /// <summary>세션·요약·raw 저장 루트 디렉터리.</summary>
    public string DataPath { get; set; } = "data";

    /// <summary>gzip 수신 본문 상한(바이트). 초과 시 413.</summary>
    public long MaxBodyBytes { get; set; } = 5242880L;

    /// <summary>수신 원문을 raw 디렉터리에 보존할 여부.</summary>
    public bool KeepRawJsonl { get; set; } = true;

    /// <summary>raw 보존 일수. 기동 시 경과분을 삭제한다.</summary>
    public int RawRetentionDays { get; set; } = 90;
}

/// <summary>
/// 업로드 응답 본문. sessionId와 저장 accepted 건수, 정제 요약을 담는다.
/// 재전송으로 교체된 경우에만 Replaced를 직렬화해 클라이언트가 교체 여부를 알 수 있게 한다.
/// </summary>
public sealed class UploadResponse
{
    /// <summary>처리한 세션 식별자.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>CSV에 기록한 로그 행 수.</summary>
    public int Accepted { get; set; }

    /// <summary>동일 sessionId 파일이 이미 있어 교체된 경우 true. 신규 응답에서는 직렬화하지 않는다.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Replaced { get; set; }

    /// <summary>정제·집계한 세션 요약. 클라이언트가 전송 즉시 처리 결과를 확인할 수 있게 한다.</summary>
    public UploadSummary Summary { get; set; } = new UploadSummary();
}

/// <summary>
/// 수신 로그를 정제·집계한 간단 요약. 요약 CSV 한 행과 같은 수치를 JSON으로 제공한다.
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

/// <summary>
/// 로그 수집 서버 진입점. 설정 바인딩, 서비스 등록, 미들웨어와 엔드포인트 구성 후 대기한다.
/// args를 WebApplicationBuilder에 전달하고 data 디렉터리를 준비해 raw 보존 정리 후 app.Run으로 요청 대기한다.
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex _monthPattern = new(@"^\d{4}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// 서버를 기동한다. 설정과 서비스를 등록하고 /api/logs, /health, /api/summary/{yyyy-MM}.csv를 연결한 뒤 요청을 처리한다.
    /// args를 받아들이며, data 디렉터리를 만들고 만료된 raw 파일을 삭제한 후 현재 스레드에서 계속 대기한다.
    /// </summary>
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        ServerOptions options = builder.Configuration.Get<ServerOptions>() ?? new ServerOptions();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<CsvSessionStore>();
        builder.Services.AddSingleton<SessionSummary>();
        builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = options.MaxBodyBytes;
        });

        WebApplication app = builder.Build();

        app.UseMiddleware<ApiKeyAuth>();
        app.MapPost("/api/logs", HandleUploadAsync);
        app.MapGet("/health", HandleHealth);
        app.MapGet("/api/summary/{month}.csv", HandleSummary);

        PrepareStorage(options, app.Services.GetRequiredService<CsvSessionStore>(), app.Logger);

        app.Run();
    }

    /// <summary>
    /// POST /api/logs 처리기. 본문 크기 검사 후 gzip 해제, JSON 역직렬화, 세션 CSV 교체 기록, raw 보존, 요약 upsert를 수행한다.
    /// context의 본문과 헤더를 사용하며 신규는 202, 동일 세션 교체는 200 응답과 sessionId, accepted 건수를 반환한다.
    /// </summary>
    private static async Task<IResult> HandleUploadAsync(
        HttpContext context,
        ServerOptions options,
        CsvSessionStore sessionStore,
        SessionSummary summary)
    {
        (bool tooLarge, byte[] body) = await ReadBodyAsync(context.Request, options.MaxBodyBytes, context.RequestAborted);
        if (tooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        bool bodyIsGzip = string.Equals(context.Request.Headers.ContentEncoding.ToString(), "gzip", StringComparison.OrdinalIgnoreCase);
        byte[] json;
        try
        {
            json = bodyIsGzip ? GzipDecompress(body) : body;
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "gzip decompress failed" });
        }

        UploadBatch? batch;
        try
        {
            batch = JsonSerializer.Deserialize<UploadBatch>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "invalid json body" });
        }

        if (batch is null || !CsvSessionStore.IsValidSessionId(batch.SessionId))
        {
            return Results.BadRequest(new { error = "missing or invalid sessionId" });
        }

        bool replaced = sessionStore.SaveSession(batch);
        sessionStore.SaveRaw(batch.SessionId, body, bodyIsGzip);
        _ = summary.Upsert(batch);

        int accepted = batch.Entries?.Length ?? 0;
        UploadSummary info = summary.Describe(batch);
        Console.WriteLine(
            $"upload session={batch.SessionId} entries={accepted} replaced={replaced} month={info.Month} gzip={bodyIsGzip}");

        UploadResponse response = new()
        {
            SessionId = batch.SessionId,
            Accepted = accepted,
            Replaced = replaced ? true : null,
            Summary = info,
        };

        return Results.Json(response, _jsonOptions, statusCode: replaced ? 200 : StatusCodes.Status202Accepted);
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
    /// gzip 바이트를 평문 바이트로 해제한다.
    /// compressed를 입력으로 사용하며 해제된 바이트를 반환하고, 형식이 깨졌으면 InvalidDataException을 던진다.
    /// </summary>
    private static byte[] GzipDecompress(byte[] compressed)
    {
        using MemoryStream input = new(compressed);
        using GZipStream gzip = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        gzip.CopyTo(output);
        return output.ToArray();
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

        logger.LogInformation(
            "LogServer 준비 완료 dataPath={DataPath} maxBodyBytes={MaxBodyBytes} keepRaw={KeepRawJsonl} expiredRawDeleted={ExpiredRawDeleted}",
            options.DataPath,
            options.MaxBodyBytes,
            options.KeepRawJsonl,
            deleted);
    }
}
