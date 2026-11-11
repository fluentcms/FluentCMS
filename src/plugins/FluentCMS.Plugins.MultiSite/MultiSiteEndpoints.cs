using FluentCMS.Sites;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins.MultiSite;

// /api/management/v1/sites, beside the routes of core, which read and change a site and its locales. The service and core
// check sites.manage for the instance on every route here.
internal static class MultiSiteEndpoints
{
    public static RouteGroupBuilder MapMultiSiteEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("", async (SiteCreateRequest request, SiteHostService service, CancellationToken cancellationToken) =>
        {
            var site = await service.CreateSiteAsync(request, cancellationToken);
            return Results.Created($"sites/{site.Id:D}", site);
        }).Produces<SiteInfo>(StatusCodes.Status201Created);

        group.MapDelete("/{id:guid}", async (Guid id, SiteHostService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteSiteAsync(id, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/hosts", async (Guid id, SiteHostService service, CancellationToken cancellationToken) =>
            Results.Ok(new { items = await service.ListAsync(id, cancellationToken) }));

        group.MapPost("/{id:guid}/hosts", async (Guid id, SiteHostRequest request, SiteHostService service, CancellationToken cancellationToken) =>
        {
            var host = await service.AddAsync(id, request, cancellationToken);
            return Results.Created($"sites/{id:D}/hosts/{host.Hostname}", host);
        }).Produces<SiteHostInfo>(StatusCodes.Status201Created);

        group.MapPatch("/{id:guid}/hosts/{hostname}", async (Guid id, string hostname, SiteHostChange change, SiteHostService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(id, hostname, change, cancellationToken))).Produces<SiteHostInfo>();

        group.MapDelete("/{id:guid}/hosts/{hostname}", async (Guid id, string hostname, SiteHostService service, CancellationToken cancellationToken) =>
        {
            await service.RemoveAsync(id, hostname, cancellationToken);
            return Results.NoContent();
        });

        return group;
    }
}
