using FluentCMS.Services;
using FluentCMS.Services.Exceptions;
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
            // resolve target-site
            var targetMethodName = ex.TargetSite.Name;
            var targetTypeName = ex.TargetSite.DeclaringType.Name;
            var targetSite = $"{targetTypeName}.{targetMethodName}";

            if (ex is AppException appEx)
            {
                _logger.LogDebug("Application Error in {TargetSite}", targetSite);

                context.Response.StatusCode = (int)appEx.Errors.First().Type;
                await context.Response.WriteAsJsonAsync(new Models.ApiResult
                {
                    Errors = appEx.Errors.Select(e => new Models.Error
                    {
                        Code = e.Code,
                        Description = e.Description ?? "", //todo: should be replaced with localized rendered message
                    }).ToList()
                });
            }
            else
            {
                _logger.LogError(ex, "Unhandled Error in {TargetSite}", targetSite);

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
