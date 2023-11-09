using FluentCMS.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Api.Middlewares;

public class ErrorMiddleware : IMiddleware
{
    private readonly ILogger<ErrorMiddleware> _logger;
    private readonly IOptions<ErrorMiddlewareOptions> _options;
    public ErrorMiddleware(
        ILogger<ErrorMiddleware> logger,
        IOptions<ErrorMiddlewareOptions> options)
    {
        _logger = logger;
        _options = options;
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
                context.Response.StatusCode = (int)appEx.ErrorCode.Type;
                await context.Response.WriteAsJsonAsync(new Models.ApiResult
                {
                    Errors = new List<Models.Error>()
                    {
                        new Models.Error
                        {
                            Code = appEx.ErrorCode.ToString(),
                            Description = ex.Message, //todo: should be replaced with localized rendered message
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
                            Details = _options.Value != null && _options.Value.IncludeErrorDebugInResponse == true
                                ? ex.Message
                                : null,
                        }
                    }
                });
            }
        }
    }
}
