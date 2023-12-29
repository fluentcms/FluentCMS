using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace FluentCMS.Web.Api.Middleware;

public class ApiExecutionContextHandlerMiddleware
{
    private readonly RequestDelegate _next;

    public ApiExecutionContextHandlerMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext, ApiExecutionContext apiExecutionContext)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex, apiExecutionContext, startTime);
            throw;
        }

    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception, ApiExecutionContext apiExecutionContext, DateTime startTime)
    {
        var apiResult = new ExceptionApiResult
        {
            Errors = new List<Error> { },
            TraceId = apiExecutionContext.TraceId,
            UniqueId = apiExecutionContext.UniqueId,
            SessionId = apiExecutionContext.SessionId,
            Duration = (DateTime.UtcNow - startTime).TotalMilliseconds,
            Code = 500 // Internal Server Error
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = apiResult.Code;

        return context.Response.WriteAsync(JsonSerializer.Serialize(apiResult));
    }

    private class ExceptionApiResult : ApiResult<object>
    {
    }
}
