namespace LogServer;

/// <summary>
/// /api로 시작하는 요청에 X-Api-Key 헤더를 요구하는 인증 미들웨어.
/// context의 헤더를 options.ApiKey와 비교하며, 불일치하면 401 응답으로 파이프라인을 중단한다.
/// </summary>
public sealed class ApiKeyAuth
{
    private readonly RequestDelegate _next;
    private readonly ServerOptions _options;

    /// <summary>
    /// 다음 미들웨어와 서버 설정을 받아 인증 미들웨어를 만든다.
    /// next와 options를 입력으로 사용하며 인스턴스 상태에 보관한다.
    /// </summary>
    public ApiKeyAuth(RequestDelegate next, ServerOptions options)
    {
        _next = next;
        _options = options;
    }

    /// <summary>
    /// /api 경로 요청의 X-Api-Key를 설정된 키와 정확히 비교한다.
    /// context를 입력으로 사용하며, 키가 없거나 다르면 401 응답을 쓰고 정지, 맞으면 _next를 호출한다.
    /// health 경로는 검증하지 않는다.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            string? provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(_options.ApiKey) || !string.Equals(provided, _options.ApiKey, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("unauthorized");
                return;
            }
        }

        await _next(context);
    }
}
