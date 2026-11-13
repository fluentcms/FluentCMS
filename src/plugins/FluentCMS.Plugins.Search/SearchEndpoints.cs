using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluentCMS.Plugins.Search;

// /api/management/v1/search. The service checks search.manage for the site of the request.
internal static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder group)
    {
        // The body is optional: without one, or without a type, every type with a searchable field is rebuilt.
        group.MapPost("/rebuild", async (SearchRebuildRequest? request, SearchService search, CancellationToken cancellationToken) =>
            Results.Accepted(value: await search.RebuildAsync(request, cancellationToken)))
            .Produces<SearchRebuildQueued>(StatusCodes.Status202Accepted);

        return group;
    }
}
