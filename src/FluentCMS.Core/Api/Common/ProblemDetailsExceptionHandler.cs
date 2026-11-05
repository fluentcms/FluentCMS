using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Api.Common;

internal sealed partial class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    private const string ContentType = "application/problem+json";

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

        var details = new ProblemDetails
        {
            Type = ProblemMapper.TypePrefix + problem.Code,
            Title = problem.Title,
            Status = problem.Status,
            Detail = problem.Detail,
        };
        details.Extensions["code"] = problem.Code;
        if (problem.Errors is not null)
        {
            details.Extensions["errors"] = problem.Errors;
        }

        if (problem.Current is not null)
        {
            details.Extensions["current"] = problem.Current;
        }

        details.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status;
        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details,
            Exception = exception,
        });

        // The service declines a request whose Accept header excludes JSON, but an error is always problem+json.
        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(details, options: null, contentType: ContentType, cancellationToken);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An unhandled exception became {Code}.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string code);
}
