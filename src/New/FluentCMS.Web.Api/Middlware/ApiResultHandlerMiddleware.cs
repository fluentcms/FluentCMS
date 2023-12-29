using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Middleware;

public class ApiResultHandlerMiddleware
{
    private readonly RequestDelegate _next;

    public ApiResultHandlerMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            //await HandleExceptionAsync(httpContext, ex);
        }
    }

    //private Task HandleExceptionAsync(HttpContext context, Exception exception)
    //{
    //    context.Response.ContentType = "application/json";
    //    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

    //    return context.Response.WriteAsync(new ErrorDetails
    //    {
    //        StatusCode = context.Response.StatusCode,
    //        Message = "An internal server error occurred."
    //    }.ToString());
    //}
}
