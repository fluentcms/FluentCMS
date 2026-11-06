namespace FluentCMS.Schema;

/// <summary>
/// The schema as the principal of the current operation may see it: the content types it may read in the site and locale
/// of the operation, without the fields outside its field allow-lists, and the components and blocks that what is left
/// uses. A relation to a type it may not read is left out, so that no name of such a type shows. The Management API
/// serves this at <c>/schema</c>; outside an operation the caller is the host's own code and sees the whole schema.
/// </summary>
public interface ISchemaIntrospection
{
    /// <summary>Describes the schema the principal may see. It needs the permission <c>schema.read</c> in the site of the operation.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The description.</returns>
    Task<SchemaDescription> DescribeAsync(CancellationToken cancellationToken = default);

    /// <summary>Describes one collection or singleton. It needs the permission <c>schema.read</c> in the site of the operation.</summary>
    /// <param name="name">The type name, in lower kebab-case.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The description, or <see langword="null"/> when there is no such type or the principal may not read it, which are not told apart.</returns>
    Task<SchemaTypeDescription?> DescribeTypeAsync(string name, CancellationToken cancellationToken = default);
}
