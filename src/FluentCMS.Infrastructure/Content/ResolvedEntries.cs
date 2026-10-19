using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Content;

// An agent resolves entries only of the types it may read (MCP-05). Fact values a view inlines from other types belong to the view.
internal sealed class ResolvedEntries(FluentCmsDbContext context, IAgentPermissions permissions) : IResolvedEntries
{
    // Until multi-site (MST-01), every entry belongs to the one site (SCH-07).
    private static readonly Guid SiteId = Site.Default.Id;

    public async Task<Result<ResolvedEntry>> ResolveAsync(Guid entryId, int? version = null, CancellationToken cancellationToken = default)
    {
        var entry = await (
            from row in context.Entries.AsNoTracking()
            join type in context.ContentTypes.AsNoTracking() on row.ContentTypeId equals type.Id
            where row.Id == entryId && row.SiteId == SiteId && row.DeletedAt == null
            select new { row.LatestVersion, type.Slug })
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return Result<ResolvedEntry>.Failure(ContentErrorCodes.EntryNotFound, $"There is no entry {entryId} in this site.");
        }

        var allowed = await permissions.CheckContentAsync(entry.Slug, AgentOperation.Read, cancellationToken: cancellationToken);
        if (!allowed.IsSuccess)
        {
            return Result<ResolvedEntry>.Failure(allowed.Errors);
        }

        var number = version ?? entry.LatestVersion;
        if (!await context.EntryVersions.AnyAsync(row => row.EntryId == entryId && row.Version == number, cancellationToken))
        {
            return Result<ResolvedEntry>.Failure(
                ContentErrorCodes.VersionNotFound,
                $"Entry {entryId} has no version {number}; its latest version is {entry.LatestVersion}.");
        }

        var resolved = await EntryResolution.ResolveAsync(context, [new VersionToResolve(entryId, entry.Slug, number)], cancellationToken);
        return Result<ResolvedEntry>.Success(resolved[0]);
    }
}
