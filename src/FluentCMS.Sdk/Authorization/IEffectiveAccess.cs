using FluentCMS.Schema;

namespace FluentCMS.Authorization;

/// <summary>
/// Describes what the principal of the current operation may do in its site, for a surface that offers only what a call
/// would allow, such as the tool list of the MCP server or a menu. It decides nothing: the services check every call, and
/// what is described here may still be refused for one entry, by a row rule or the locales of a grant.
/// </summary>
public interface IEffectiveAccess
{
    /// <summary>Describes what the principal may do with content, in the site and locale of the operation.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The content types the principal may read, and what it may do with each.</returns>
    ValueTask<ContentAccess> ContentAsync(CancellationToken cancellationToken = default);

    /// <summary>Says which permissions the principal holds in the site of the operation.</summary>
    /// <param name="permissions">The names of the permissions asked about, such as <c>media.upload</c>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The permissions of those asked about that a grant gives the principal there, a grant limited to resources such as
    /// folders included, since some call with it may then be allowed.
    /// </returns>
    ValueTask<IReadOnlySet<string>> HeldAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default);
}

/// <summary>What a principal may do with content in a site and locale.</summary>
public sealed record ContentAccess
{
    /// <summary>
    /// The schema as the principal may read it: the content types it may read with the fields it may read, narrowed as
    /// <see cref="ISchemaIntrospection"/> narrows it, without the permission to read the whole schema.
    /// </summary>
    public required SchemaModel Schema { get; init; }

    /// <summary>What the principal may do with each content type of <see cref="Schema"/>, by the name of the type.</summary>
    public required IReadOnlyDictionary<string, ContentTypeAccess> Types { get; init; }

    /// <summary>Whether a query may search by text: the <see cref="Providers.ISearchIndex"/> slot is filled.</summary>
    public bool Search { get; init; }
}

/// <summary>What a principal may do with the entries of a content type it may read.</summary>
public sealed record ContentTypeAccess
{
    /// <summary>Whether the principal may read drafts, and not only what is published.</summary>
    public bool Drafts { get; init; }

    /// <summary>Whether a grant lets the principal create entries.</summary>
    public bool Create { get; init; }

    /// <summary>Whether a grant lets the principal update entries.</summary>
    public bool Update { get; init; }

    /// <summary>Whether a grant lets the principal move entries to the trash.</summary>
    public bool Delete { get; init; }

    /// <summary>Whether a grant lets the principal publish entries.</summary>
    public bool Publish { get; init; }

    /// <summary>Whether a grant lets the principal unpublish entries.</summary>
    public bool Unpublish { get; init; }

    /// <summary>The readable fields that some grant lets the principal set on a new entry.</summary>
    public required IReadOnlySet<string> CreatableFields { get; init; }

    /// <summary>The readable fields that some grant lets the principal change on an entry.</summary>
    public required IReadOnlySet<string> UpdatableFields { get; init; }
}
