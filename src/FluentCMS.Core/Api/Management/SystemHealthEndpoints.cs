using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluentCMS.Core.Api.Management;

// GET /api/management/v1/system/health: every check, live and ready, with its duration. It needs system.read at the
// instance level, so a role of one site and a key bound to sites are refused. It answers 200 whatever the status, because
// the body is the report; the probes under /health are the ones that answer 503.
internal static class SystemHealthEndpoints
{
    public static IServiceCollection AddSystemHealthApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management) =>
        management.MapGroup("/system").WithFluentCmsOperation(Origin.Api).MapGet("/health", GetAsync);

    private static async Task<IResult> GetAsync(HttpContext httpContext, [FromServices] IAuthorizationEvaluator evaluator, [FromServices] HealthCheckService health)
    {
        var operation = ApiRequest.Of(httpContext).Operation;
        var permissions = await evaluator.ResolveAsync(operation, httpContext.RequestAborted);
        if (!permissions.Allows(CorePermissions.SystemRead, new AccessTarget(Site: null, Locale: null)))
        {
            throw new PermissionDeniedException(CorePermissions.SystemRead);
        }

        var report = await health.CheckHealthAsync(httpContext.RequestAborted);
        return Results.Json(Report(report));
    }

    // A description or exception may quote a connection string, so every text goes through the scrubber.
    private static JsonObject Report(HealthReport report) => new()
    {
        ["status"] = report.Status.ToString(),
        ["totalDurationMs"] = report.TotalDuration.TotalMilliseconds,
        ["checks"] = new JsonArray([.. report.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => Check(entry.Key, entry.Value))]),
    };

    private static JsonObject Check(string name, HealthReportEntry entry) => new()
    {
        ["name"] = name,
        ["status"] = entry.Status.ToString(),
        ["durationMs"] = entry.Duration.TotalMilliseconds,
        ["tags"] = new JsonArray([.. entry.Tags.Order(StringComparer.Ordinal).Select(tag => (JsonNode?)JsonValue.Create(tag))]),
        ["description"] = entry.Description is { } description ? SecretScrubber.Of(description) : null,
        ["error"] = entry.Exception is { } exception ? SecretScrubber.Of($"{exception.GetType().Name}: {exception.Message}") : null,
    };
}
