using FluentCMS.Core.Telemetry;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Counts the requests to FluentCMS endpoints by surface and status class. It sits right after routing, so a request the
// CORS, rate limit or request limit layers turn away is counted too.
internal sealed class RequestMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<FluentCmsSurfaceMetadata>() is not { } surface)
        {
            await next(context);
            return;
        }

        var completed = false;
        try
        {
            await next(context);
            completed = true;
        }
        finally
        {
            var status = completed ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
            FluentCmsTelemetry.Requests.Add(
                1,
                new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Surface, surface.Name),
                new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.StatusClass, $"{status / 100}xx"));
        }
    }
}
