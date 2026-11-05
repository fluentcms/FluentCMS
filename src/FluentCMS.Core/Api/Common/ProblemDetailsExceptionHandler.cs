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

        await ProblemWriter.WriteAsync(httpContext, problem, exception, cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An unhandled exception became {Code}.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string code);
}
