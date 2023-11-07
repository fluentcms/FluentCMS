using FluentCMS.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Api.Middlewares;

public class ErrorMiddleware : IMiddleware
{
    private readonly ILogger<ErrorMiddleware> _logger;
    public ErrorMiddleware(ILogger<ErrorMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next.Invoke(context);
        }
        catch (Exception ex)
        {
            if (ex is AppException appEx)
            {
                context.Response.StatusCode = (int)appEx.Type;
                await context.Response.WriteAsJsonAsync(new Models.ApiResult
                {
                    Errors = new List<Models.Error>()
                    {
                        new Models.Error
                        {
                            Code = ex.HResult.ToString(),
                            Description = ex.Message,
                        }
                    }
                });
            }
            else
            {
                _logger.LogError(ex, ex.Message);

                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new Models.ApiResult
                {
                    Errors = new List<Models.Error>()
                    {
                        new Models.Error
                        {
                            Code = "500",
                            Description = "Fatal error",
                        }
                    }
                });
            }
        }
    }
}
