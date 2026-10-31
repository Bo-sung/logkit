using Microsoft.AspNetCore.Http;

namespace LogServer;

/// <summary>
/// /api로 시작하는 요청에 X-Api-Key 헤더를 요구하는 인증 미들웨어. 수신 서버와 분석 서버가 함께 쓴다.
/// 요청 종류에 따라 필요한 역할을 정한다(조회는 열람, POST /api/logs는 업로드, 그 밖의 변경은 관리).
/// 키가 없거나 어떤 역할의 키와도 맞지 않으면 401, 키는 맞지만 역할이 모자라면 403으로 응답한다.
/// </summary>
public sealed class ApiKeyAuth
{
    /// <summary>업로드 요청의 프로젝트 이름을 HttpContext.Items에 담는 키. 값이 없으면 프로젝트 없는 요청이다.</summary>
    public const string ProjectItem = "LogProject";

    private readonly RequestDelegate _next;
    private readonly ApiKeys _keys;

    /// <summary>
    /// 다음 미들웨어와 역할별 키를 받아 인증 미들웨어를 만든다.
    /// next와 keys를 입력으로 사용하며 인스턴스 상태에 보관한다.
    /// </summary>
    public ApiKeyAuth(RequestDelegate next, ApiKeys keys)
    {
        _next = next;
        _keys = keys;
    }

    /// <summary>
    /// /api 경로 요청의 X-Api-Key 헤더 또는 key 쿼리 매개변수를 검사한다.
    /// 쿼리 매개변수는 브라우저 다운로드 링크처럼 헤더를 붙일 수 없는 요청을 위한 보조 수단이다.
    /// context를 입력으로 사용하며, 통과하면 _next를 호출하고 아니면 401 또는 403 응답을 쓰고 멈춘다.
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

            ApiRole role = RoleFor(context.Request);
            if (!_keys.IsAllowed(provided, role))
            {
                context.Response.StatusCode = _keys.IsKnown(provided)
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync(_keys.IsKnown(provided) ? "forbidden" : "unauthorized");
                return;
            }

            if (role == ApiRole.Upload && _keys.ProjectFor(provided) is { } project)
            {
                context.Items[ProjectItem] = project;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// 요청이 필요로 하는 역할을 돌려준다. 조회는 Read, POST /api/logs는 Upload, 나머지 변경은 Admin이다.
    /// request를 입력으로 사용하며 상태를 바꾸지 않는다.
    /// </summary>
    private static ApiRole RoleFor(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return ApiRole.Read;
        }

        if (HttpMethods.IsPost(request.Method) && request.Path.Equals("/api/logs", StringComparison.OrdinalIgnoreCase))
        {
            return ApiRole.Upload;
        }

        return ApiRole.Admin;
    }
}
