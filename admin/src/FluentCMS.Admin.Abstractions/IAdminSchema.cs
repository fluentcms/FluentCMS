using FluentCMS.Admin.Api;

namespace FluentCMS.Admin;

/// <summary>
/// The schema of the site the shell shows, in the content locale, as the admin read it with <c>GET /schema</c>: the types the
/// user may read, the components and the blocks. A field editor that holds blocks or links of its own, such as rich text, reads
/// it here rather than again.
/// </summary>
public interface IAdminSchema
{
    /// <summary>Gets the schema, read once for the site and the locale and kept.</summary>
    /// <param name="cancellationToken">Stops waiting for it.</param>
    /// <returns>The schema.</returns>
    Task<SchemaDescription> GetSchemaAsync(CancellationToken cancellationToken = default);
}
