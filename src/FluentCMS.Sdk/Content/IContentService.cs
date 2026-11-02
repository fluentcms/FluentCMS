using FluentCMS.Schema;

namespace FluentCMS.Content;

/// <summary>
/// The only path to content for HTTP, GraphQL, MCP and host code. Every operation runs in the ambient
/// <see cref="OperationContext"/>, which gives the principal, site and locale, and applies permissions, field limits,
/// hooks, validation and versions. This form works on <see cref="ContentDocument"/> and names the type on each call.
/// </summary>
public interface IContentService
{
    /// <summary>Reads one entry.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="id">The id of the entry.</param>
    /// <param name="options">What to read, or <see langword="null"/> for the published entry in the locale of the operation.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry, or <see langword="null"/> when it does not exist or the principal may not read it.</returns>
    /// <exception cref="ValidationException">The options are not valid, or the principal may not read the type or its drafts.</exception>
    Task<ContentDocument?> GetAsync(string type, Guid id, GetOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a page of entries.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="query">What to read. A missing site or locale is that of the operation. Another site than that of the operation is refused, except for the system principal.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ValidationException">The query is not valid, or the principal may not read the type or its drafts.</exception>
    Task<PagedResult<ContentDocument>> QueryAsync(string type, ContentQuery query, CancellationToken cancellationToken = default);

    /// <summary>Creates the draft of a new entry in the site and locale of the operation.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="entry">The values. System fields are set by the engine.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not create entries of the type.</exception>
    /// <exception cref="ValidationException">A value is refused or not valid.</exception>
    Task<ContentDocument> CreateAsync(string type, ContentDocument entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the draft in the locale of the operation. A field left out is cleared, unless the principal may not write
    /// or read it, in which case it keeps its value; so do relation targets it may not read. Fields named under
    /// <c>fallbacks</c> count as left out. With an expected version of 0, this adds a locale the entry does not have yet.
    /// </summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="id">The id of the entry.</param>
    /// <param name="entry">The values. System fields are set by the engine.</param>
    /// <param name="expectedVersion">The version the change was based on.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not update entries of the type.</exception>
    /// <exception cref="EntryNotFoundException">The entry does not exist or the principal may not reach it.</exception>
    /// <exception cref="VersionConflictException">The stored version is not the expected one.</exception>
    /// <exception cref="ValidationException">A value is refused or not valid.</exception>
    Task<ContentDocument> UpdateAsync(string type, Guid id, ContentDocument entry, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>Changes the draft in the locale of the operation with a JSON merge patch: a null removes a field, and an object merges into a single component or a JSON field.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="id">The id of the entry.</param>
    /// <param name="patch">The changes.</param>
    /// <param name="expectedVersion">The version the change was based on.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not update entries of the type.</exception>
    /// <exception cref="EntryNotFoundException">The entry does not exist or the principal may not reach it.</exception>
    /// <exception cref="VersionConflictException">The stored version is not the expected one.</exception>
    /// <exception cref="ValidationException">A value is refused or not valid.</exception>
    Task<ContentDocument> PatchAsync(string type, Guid id, ContentDocument patch, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>Moves an entry, in all its locales, to the trash.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="id">The id of the entry.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the entry is in the trash.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not delete the entry in every one of its locales.</exception>
    /// <exception cref="EntryNotFoundException">The entry does not exist, is in the trash already, or the principal may not reach it.</exception>
    Task DeleteAsync(string type, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Brings an entry, in all its locales, back from the trash.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="id">The id of the entry.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not restore the entry in every one of its locales.</exception>
    /// <exception cref="EntryNotFoundException">The entry is not in the trash, or the principal may not reach it.</exception>
    Task<ContentDocument> RestoreAsync(string type, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Counts the entries that match a filter in the site and locale of the operation.</summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="filter">The filter, or <see langword="null"/> for every entry.</param>
    /// <param name="status">Which state of the entries to count.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The number of entries the principal may read.</returns>
    /// <exception cref="ValidationException">The filter is not valid, or the principal may not read the type or its drafts.</exception>
    Task<long> CountAsync(string type, ContentFilter? filter = null, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IContentService"/> for one content class. It maps the class to and from <see cref="ContentDocument"/> and
/// goes through the same operations. Its system fields are set by the engine, and what they hold on a write is ignored.
/// </summary>
/// <typeparam name="T">The content class.</typeparam>
public interface IContentService<T>
    where T : ContentItem
{
    /// <summary>Reads one entry.</summary>
    /// <param name="id">The id of the entry.</param>
    /// <param name="options">What to read, or <see langword="null"/> for the published entry in the locale of the operation. A class cannot hold every locale or rendered rich text, so these are refused.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry, or <see langword="null"/> when it does not exist or the principal may not read it.</returns>
    /// <exception cref="ValidationException">The options are not valid, or the principal may not read the type or its drafts.</exception>
    Task<T?> GetAsync(Guid id, GetOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a page of entries.</summary>
    /// <param name="query">What to read. A missing site or locale is that of the operation. A class cannot hold every locale or rendered rich text, so these are refused.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ValidationException">The query is not valid, or the principal may not read the type or its drafts.</exception>
    Task<PagedResult<T>> QueryAsync(ContentQuery query, CancellationToken cancellationToken = default);

    /// <summary>Creates the draft of a new entry in the site and locale of the operation.</summary>
    /// <param name="entry">The values.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not create entries of the type.</exception>
    /// <exception cref="ValidationException">A value is refused or not valid.</exception>
    Task<T> CreateAsync(T entry, CancellationToken cancellationToken = default);

    /// <summary>Replaces the draft in the locale of the operation. A property with no value clears its field. A property the principal may not read, and one that is hidden, is not sent, so it keeps its value; use a patch to write a field that cannot be read back.</summary>
    /// <param name="id">The id of the entry.</param>
    /// <param name="entry">The values.</param>
    /// <param name="expectedVersion">The version the change was based on.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not update entries of the type.</exception>
    /// <exception cref="EntryNotFoundException">The entry does not exist or the principal may not reach it.</exception>
    /// <exception cref="VersionConflictException">The stored version is not the expected one.</exception>
    /// <exception cref="ValidationException">A value is refused or not valid.</exception>
    Task<T> UpdateAsync(Guid id, T entry, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>Changes the draft in the locale of the operation with a JSON merge patch. A patch names fields, which a class cannot do, so it is a document.</summary>
    /// <param name="id">The id of the entry.</param>
    /// <param name="patch">The changes.</param>
    /// <param name="expectedVersion">The version the change was based on.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not update entries of the type.</exception>
    /// <exception cref="EntryNotFoundException">The entry does not exist or the principal may not reach it.</exception>
    /// <exception cref="VersionConflictException">The stored version is not the expected one.</exception>
    /// <exception cref="ValidationException">A value is refused or not valid.</exception>
    Task<T> PatchAsync(Guid id, ContentDocument patch, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>Moves an entry, in all its locales, to the trash.</summary>
    /// <param name="id">The id of the entry.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the entry is in the trash.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not delete the entry in every one of its locales.</exception>
    /// <exception cref="EntryNotFoundException">The entry does not exist, is in the trash already, or the principal may not reach it.</exception>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Brings an entry, in all its locales, back from the trash.</summary>
    /// <param name="id">The id of the entry.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entry with its system fields, without what the principal may not read.</returns>
    /// <exception cref="ContentAccessDeniedException">The principal may not restore the entry in every one of its locales.</exception>
    /// <exception cref="EntryNotFoundException">The entry is not in the trash, or the principal may not reach it.</exception>
    Task<T> RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Counts the entries that match a filter in the site and locale of the operation.</summary>
    /// <param name="filter">The filter, or <see langword="null"/> for every entry.</param>
    /// <param name="status">Which state of the entries to count.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The number of entries the principal may read.</returns>
    /// <exception cref="ValidationException">The filter is not valid, or the principal may not read the type or its drafts.</exception>
    Task<long> CountAsync(ContentFilter? filter = null, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default);
}
