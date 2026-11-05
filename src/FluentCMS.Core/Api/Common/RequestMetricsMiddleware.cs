using FluentCMS.Core.Telemetry;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Counts the requests to FluentCMS endpoints by surface and status class. It sits right after routing, so a request the
// CORS, rate limit or request limit layers turn away is counted too. The exception handler is outside it and clears the
// endpoint before it writes, so an exception is counted with the status the handler is about to write for it.
internal sealed class RequestMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<FluentCmsSurfaceMetadata>() is not { } surface)
        {
            await next(context);
            return;
        }

        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            status = context.Response.StatusCode;
        }
        catch (Exception exception)
        {
            status = ProblemMapper.Map(exception, context.User.Identity?.IsAuthenticated == true).Status;
            throw;
        }
        finally
        {
            FluentCmsTelemetry.Requests.Add(
                1,
                new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Surface, surface.Name),
                new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.StatusClass, FluentCmsTelemetry.StatusClass(status)));
        }
    }
}
