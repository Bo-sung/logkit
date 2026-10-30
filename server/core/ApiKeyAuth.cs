using Microsoft.AspNetCore.Http;

namespace LogServer;

/// <summary>
/// /api로 시작하는 요청에 X-Api-Key 헤더를 요구하는 인증 미들웨어. 수신 서버와 분석 서버가 함께 쓴다.
/// 요청의 X-Api-Key 헤더(또는 key 쿼리)를 설정된 키와 비교하며, 불일치하면 401 응답으로 파이프라인을 중단한다.
/// </summary>
public sealed class ApiKeyAuth
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    /// <summary>
    /// 다음 미들웨어와 기대하는 키를 받아 인증 미들웨어를 만든다.
    /// next와 apiKey를 입력으로 사용하며 인스턴스 상태에 보관한다.
    /// </summary>
    public ApiKeyAuth(RequestDelegate next, string apiKey)
    {
        _next = next;
        _apiKey = apiKey;
    }

    /// <summary>
    /// /api 경로 요청의 X-Api-Key 헤더 또는 key 쿼리 매개변수를 설정된 키와 정확히 비교한다.
    /// 쿼리 매개변수는 브라우저 다운로드 링크처럼 헤더를 붙일 수 없는 요청을 위한 보조 수단이다.
    /// context를 입력으로 사용하며, 키가 없거나 다르면 401 응답을 쓰고 정지, 맞으면 _next를 호출한다.
    /// health와 대시보드 페이지 경로는 검증하지 않는다.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            string? provided = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(provided))
            {
                provided = context.Request.Query["key"].FirstOrDefault();
            }

            if (string.IsNullOrEmpty(_apiKey) || !string.Equals(provided, _apiKey, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("unauthorized");
                return;
            }
        }

        await _next(context);
    }
}
