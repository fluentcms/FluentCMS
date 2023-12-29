//using Microsoft.AspNetCore.Http;

//namespace FluentCMS.Web.Api.Middleware;

//public class ApiExecutionContextHandlerMiddleware
//{
//    private readonly RequestDelegate _next;

//    public ApiExecutionContextHandlerMiddleware(RequestDelegate next)
//    {
//        _next = next;
//    }

//    public async Task InvokeAsync(HttpContext httpContext, ApiExecutionContext apiExecutionContext)
//    {
//        httpContext.Items["ApiExecutionContext"] = apiExecutionContext;
//        await _next(httpContext);
//    }
//}
