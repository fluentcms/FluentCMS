using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Testing;

internal sealed class ScopedContentService<T>(OperationRunner runner) : IContentService<T>
    where T : ContentItem
{
    public Task<T?> GetAsync(Guid id, GetOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T?>(service => service.GetAsync(id, options, cancellationToken));

    public Task<PagedResult<T>> QueryAsync(ContentQuery query, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, PagedResult<T>>(service => service.QueryAsync(query, cancellationToken));

    public Task<T> CreateAsync(T entry, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.CreateAsync(entry, cancellationToken));

    public Task<T> UpdateAsync(Guid id, T entry, int expectedVersion, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.UpdateAsync(id, entry, expectedVersion, cancellationToken));

    public Task<T> PatchAsync(Guid id, ContentDocument patch, int expectedVersion, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.PatchAsync(id, patch, expectedVersion, cancellationToken));

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>>(service => service.DeleteAsync(id, cancellationToken));

    public Task<T> RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.RestoreAsync(id, cancellationToken));

    public Task<long> CountAsync(ContentFilter? filter = null, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, long>(service => service.CountAsync(filter, status, cancellationToken));
}

internal sealed class ScopedContentService(OperationRunner runner) : IContentService
{
    public Task<ContentDocument?> GetAsync(string type, Guid id, GetOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument?>(service => service.GetAsync(type, id, options, cancellationToken));

    public Task<PagedResult<ContentDocument>> QueryAsync(string type, ContentQuery query, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, PagedResult<ContentDocument>>(service => service.QueryAsync(type, query, cancellationToken));

    public Task<ContentDocument> CreateAsync(string type, ContentDocument entry, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument>(service => service.CreateAsync(type, entry, cancellationToken));

    public Task<ContentDocument> UpdateAsync(string type, Guid id, ContentDocument entry, int expectedVersion, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument>(service => service.UpdateAsync(type, id, entry, expectedVersion, cancellationToken));

    public Task<ContentDocument> PatchAsync(string type, Guid id, ContentDocument patch, int expectedVersion, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument>(service => service.PatchAsync(type, id, patch, expectedVersion, cancellationToken));

    public Task DeleteAsync(string type, Guid id, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService>(service => service.DeleteAsync(type, id, cancellationToken));

    public Task<ContentDocument> RestoreAsync(string type, Guid id, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument>(service => service.RestoreAsync(type, id, cancellationToken));

    public Task<long> CountAsync(string type, ContentFilter? filter = null, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, long>(service => service.CountAsync(type, filter, status, cancellationToken));
}
