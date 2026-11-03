using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Localization;
using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// One code path: every operation is the document service's, with the class mapped on the way in and out. A class has a
// property for every system field, so it reads the two a document leaves out, as far as the principal may read them.
internal sealed class TypedContentService<T>(ContentService documents, SchemaModel schema, ContentEntryMapper mapper) : IContentService<T>
    where T : ContentItem
{
    private readonly string _type = schema.FindContentType(typeof(T))?.Name
        ?? throw new InvalidOperationException($"{typeof(T)} is not a content type of the schema.");

    public async Task<T?> GetAsync(Guid id, GetOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfUnfit(options?.Locale, options?.RichTextFormat);

        return await documents.GetAsync(_type, id, options, everySystemField: true, cancellationToken) is { } entry ? Map(entry) : null;
    }

    public async Task<PagedResult<T>> QueryAsync(ContentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfUnfit(query.Locale, query.RichTextFormat);

        var page = await documents.QueryAsync(_type, query, everySystemField: true, cancellationToken);
        return new PagedResult<T>(page.Items.Select(Map).ToList(), page.Total, page.Page, page.PageSize, page.NextCursor);
    }

    public async Task<T> CreateAsync(T entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var limits = await documents.TypedWriteLimitsAsync(_type, cancellationToken);
        return await ReadBackAsync(await documents.CreateAsync(_type, mapper.ToDocument(entry, static _ => true, limits.IsSystem, isUpdate: false), cancellationToken));
    }

    public async Task<T> UpdateAsync(Guid id, T entry, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // What the principal may not read came back as a default: sending it would overwrite the stored value.
        var limits = await documents.TypedWriteLimitsAsync(_type, cancellationToken);
        return await ReadBackAsync(await documents.UpdateAsync(_type, id, mapper.ToDocument(entry, limits.CanRead, limits.IsSystem, isUpdate: true), expectedVersion, cancellationToken));
    }

    public async Task<T> PatchAsync(Guid id, ContentDocument patch, int expectedVersion, CancellationToken cancellationToken = default) =>
        await ReadBackAsync(await documents.PatchAsync(_type, id, patch, expectedVersion, cancellationToken));

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.DeleteAsync(_type, id, cancellationToken);

    public async Task<T> RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ReadBackAsync(await documents.RestoreAsync(_type, id, cancellationToken));

    public async Task<PagedResult<T>> QueryTrashAsync(ContentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfUnfit(query.Locale, query.RichTextFormat);

        var page = await documents.QueryTrashAsync(_type, query, everySystemField: true, cancellationToken);
        return new PagedResult<T>(page.Items.Select(Map).ToList(), page.Total, page.Page, page.PageSize, page.NextCursor);
    }

    public Task PurgeAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.PurgeAsync(_type, id, cancellationToken);

    public async Task<PublishResult<T>> PublishAsync(Guid id, string? locale = null, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var published = await documents.PublishAsync(_type, id, locale, expectedVersion, cancellationToken);
        return new PublishResult<T>(await ReadBackAsync(published.Entry, locale), published.Warnings);
    }

    public async Task<T> UnpublishAsync(Guid id, string? locale = null, CancellationToken cancellationToken = default) =>
        await ReadBackAsync(await documents.UnpublishAsync(_type, id, locale, cancellationToken), locale);

    public async Task<T> DiscardDraftAsync(Guid id, string? locale = null, int? expectedVersion = null, CancellationToken cancellationToken = default) =>
        await ReadBackAsync(await documents.DiscardDraftAsync(_type, id, locale, expectedVersion, cancellationToken), locale);

    public Task<IReadOnlyList<VersionInfo>> GetVersionsAsync(Guid id, string? locale = null, CancellationToken cancellationToken = default) =>
        documents.GetVersionsAsync(_type, id, locale, cancellationToken);

    public async Task<T?> GetVersionAsync(Guid id, int number, string? locale = null, CancellationToken cancellationToken = default) =>
        await documents.GetVersionAsync(_type, id, number, locale, cancellationToken) is { } entry ? Map(entry) : null;

    public async Task<T> RestoreVersionAsync(Guid id, int number, string? locale = null, int? expectedVersion = null, CancellationToken cancellationToken = default) =>
        await ReadBackAsync(await documents.RestoreVersionAsync(_type, id, number, locale, expectedVersion, cancellationToken), locale);

    public Task<IReadOnlyList<VersionChange>> DiffAsync(Guid id, int fromVersion, int toVersion, string? locale = null, CancellationToken cancellationToken = default) =>
        documents.DiffAsync(_type, id, fromVersion, toVersion, locale, cancellationToken);

    public Task<long> CountAsync(ContentFilter? filter = null, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
        documents.CountAsync(_type, filter, status, cancellationToken);

    private T Map(ContentDocument entry) => (T)mapper.ToEntry(typeof(T), entry);

    // What a write returned, which is all a principal without the read gets, with the system fields a class also holds.
    // The write has committed by now, so a cancelled caller does not turn it into a failure.
    private async Task<T> ReadBackAsync(ContentDocument written, string? locale = null)
    {
        var options = new GetOptions { Status = QueryStatus.Draft, Fallback = LocaleFallback.None, Locale = locale };
        return Map(written.ContainsKey("status") && await documents.GetAsync(_type, (Guid)written["id"]!, options, everySystemField: true, CancellationToken.None) is { } entry ? entry : written);
    }

    private static void ThrowIfUnfit(string? locale, string? richTextFormat)
    {
        if (string.Equals(locale, ILocaleResolver.All, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationError("locale", QueryErrorCodes.InvalidParameter, "A content class holds one locale; read every locale as documents.")]);
        }

        if (!ResponseRichText.IsJson(richTextFormat))
        {
            throw new ValidationException([new ValidationError("richText", QueryErrorCodes.InvalidParameter, "A content class holds rich text as JSON; read rendered text as documents.")]);
        }
    }
}
