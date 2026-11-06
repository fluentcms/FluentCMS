using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Api.Common;

internal sealed partial class ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var problem = ProblemMapper.Map(exception, httpContext.User.Identity?.IsAuthenticated == true);
        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, problem.Code);
        }

        // A person reads a hosted page, so it gets a page. It is written here, not rendered, since what failed may be what a page
        // needs, such as the database its branding comes from. The handler has cleared the endpoint; the feature keeps it.
        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<HostedPageMetadata>() is not null)
        {
            await WritePageAsync(httpContext, problem, cancellationToken);
            return true;
        }

        await ProblemWriter.WriteAsync(httpContext, problem, exception, cancellationToken);
        return true;
    }

    private static Task WritePageAsync(HttpContext httpContext, ApiProblem problem, CancellationToken cancellationToken)
    {
        var response = httpContext.Response;
        response.StatusCode = problem.Status;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
        response.Headers.CacheControl = "no-store";
        var message = problem.Status >= StatusCodes.Status500InternalServerError
            ? "Something went wrong on our side. Try again in a moment."
            : "The request could not be handled. Go back and try again.";
        return response.WriteAsync(
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="robots" content="noindex, nofollow"><title>Something went wrong</title></head>
            <body style="font-family: system-ui, sans-serif; max-width: 32rem; margin: 4rem auto; padding: 0 1rem">
            <h1>Something went wrong</h1>
            <p>{message}</p>
            <p>Error: <code>{problem.Code}</code></p>
            </body>
            </html>
            """,
            cancellationToken);
    }

    [LoggerMessage(EventId = 9110, Level = LogLevel.Error, Message = "An unhandled exception became {Code}.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string code);
}
