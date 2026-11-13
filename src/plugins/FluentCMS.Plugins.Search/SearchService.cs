using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Jobs;
using FluentCMS.Schema;

namespace FluentCMS.Plugins.Search;

public sealed record SearchRebuildRequest(string? Type);

public sealed record SearchRebuildQueued(Guid JobId);

// The Management side of the plugin. Every call needs search.manage in the site of the request.
internal sealed class SearchService(IPermissionChecker permissions, IOperationContextAccessor operations, SchemaModel schema, IJobScheduler scheduler)
{
    // Queues search.rebuild for the site of the request, of one type or of every type with a searchable field.
    public async Task<SearchRebuildQueued> RebuildAsync(SearchRebuildRequest? request, CancellationToken cancellationToken)
    {
        var site = (operations.Current ?? throw new InvalidOperationException("The search service runs inside an operation.")).Site;
        await permissions.RequireAsync(SearchPermissions.Manage, site, cancellationToken);

        if (request?.Type is { } name)
        {
            if (schema.FindContentType(name) is not { } type)
            {
                throw new PluginException(SearchErrorCodes.InvalidRebuild, $"'{name}' is not a content type.");
            }

            if (SearchText.Fields(type).Count == 0)
            {
                throw new PluginException(SearchErrorCodes.NotSearchable, $"{name} has no field marked [Searchable], so it has nothing to index.");
            }
        }

        var job = await scheduler.EnqueueAsync<SearchRebuildJob, SearchRebuildPayload>(new SearchRebuildPayload(request?.Type), SearchRebuildJob.Options(site.Value), cancellationToken);
        return new SearchRebuildQueued(job);
    }
}
