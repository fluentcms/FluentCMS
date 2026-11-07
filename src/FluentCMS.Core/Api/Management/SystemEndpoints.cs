using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Api.Management;

// The system group of spec 4.10. What it shows is about the instance, so it needs a permission that no site limits.
internal static class SystemEndpoints
{
    public static IServiceCollection AddSystemApi(this IServiceCollection services) =>
        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Management, Map);

    private static void Map(RouteGroupBuilder management)
    {
        var system = management.MapGroup("/system").WithFluentCmsOperation(Origin.Api).RequireManagementScope();
        system.MapGet("/diagnostics", DiagnosticsAsync).Produces<StartupReport>();
    }

    private static async Task<IResult> DiagnosticsAsync(HttpContext httpContext, [FromServices] IAuthorizationEvaluator evaluator, [FromServices] StartupReportHolder report)
    {
        await InstanceAccess.RequireAsync(httpContext, evaluator, CorePermissions.SystemRead);
        var current = report.Current ?? throw new ServiceUnavailableException("The startup report is made when the host has started; try again in a moment.");
        return Results.Json(current, StartupReportJson.Options);
    }
}
