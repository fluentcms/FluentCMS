using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
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
        management.MapGroup("/system").WithFluentCmsOperation(Origin.Api).MapGet("/health", GetAsync).Produces<HealthReportResponse>();

    private static async Task<IResult> GetAsync(HttpContext httpContext, [FromServices] IAuthorizationEvaluator evaluator, [FromServices] HealthCheckService health)
    {
        await InstanceAccess.RequireAsync(httpContext, evaluator, CorePermissions.SystemRead);

        var report = await health.CheckHealthAsync(httpContext.RequestAborted);
        return AdminJson.Ok(HealthReportResponse.From(report));
    }
}
