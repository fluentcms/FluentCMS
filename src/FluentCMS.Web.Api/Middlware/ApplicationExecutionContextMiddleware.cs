using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Middleware;

// This middleware is used to inject the ApiExecutionContext into the HttpContext.Items collection.
// We just want to be sure that the ApiExecutionContext is initialized before we start processing the request.
public class ApplicationExecutionContextMiddleware
{
    private readonly RequestDelegate _next;

    public ApplicationExecutionContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext, ApiExecutionContext apiExecutionContext)
    {
        httpContext.Items.Add("ApiExecutionContext", apiExecutionContext);
        await _next(httpContext);
    }
}
