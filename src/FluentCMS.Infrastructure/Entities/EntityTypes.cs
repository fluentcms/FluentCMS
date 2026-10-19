using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Entities;

internal static class EntityTypes
{
    // The entity types of the site by id: types whose current definition carries the flag, as the schema reads them (SCH-03).
    public static async Task<Dictionary<Guid, string>> LoadAsync(FluentCmsDbContext context, CancellationToken cancellationToken)
    {
        var siteId = Site.Default.Id;
        var types = await (
            from type in context.ContentTypes.AsNoTracking()
            join version in context.ContentTypeVersions.AsNoTracking()
                on new { ContentTypeId = type.Id, type.Version } equals new { version.ContentTypeId, version.Version }
            where type.SiteId == siteId
            select new { type.Id, type.Slug, version.Definition })
            .ToListAsync(cancellationToken);

        return types
            .Where(type => IsEntity(type.Slug, type.Definition))
            .ToDictionary(type => type.Id, type => type.Slug);
    }

    private static bool IsEntity(string slug, string json)
    {
        var definition = ContentTypeDefinition.Parse(json);

        return definition.IsSuccess
            ? definition.Value.IsEntity
            : throw new InvalidOperationException(
                $"The current definition of content type '{slug}' is stored in a form that cannot be read: "
                + string.Join("; ", definition.Errors.Select(error => error.Message)));
    }
}
