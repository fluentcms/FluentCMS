using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Common;

// Writes an ApiProblem as application/problem+json, for the exception handler and for the middleware that answers itself.
internal static class ProblemWriter
{
    private const string ContentType = "application/problem+json";

    public static async Task WriteAsync(HttpContext httpContext, ApiProblem problem, Exception? exception = null, CancellationToken cancellationToken = default)
    {
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
            details.Extensions["current"] = ContentJson.Entry(problem.Current);
        }

        details.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status;
        var written = await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
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
    }
}
