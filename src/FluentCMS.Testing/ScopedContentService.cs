using FluentCMS.Content;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Testing;

internal sealed class ScopedContentService<T>(OperationRunner runner, IServiceProvider services) : IContentService<T>
    where T : ContentItem
{
    private string? _type;

    public string Type => _type ??= services.GetRequiredService<SchemaModel>().FindContentType(typeof(T))?.Name
        ?? throw new InvalidOperationException($"{typeof(T)} is not a content type of the schema.");

    public IContentService Documents { get; } = new ScopedContentService(runner);

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

    public Task<T> RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.RestoreAsync(id, cancellationToken));

    public Task<PagedResult<T>> QueryTrashAsync(ContentQuery query, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, PagedResult<T>>(service => service.QueryTrashAsync(query, cancellationToken));

    public Task<PublishResult<T>> PublishAsync(Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, PublishResult<T>>(service => service.PublishAsync(id, options, cancellationToken));

    public Task<T> UnpublishAsync(Guid id, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.UnpublishAsync(id, options, cancellationToken));

    public Task<T> DiscardDraftAsync(Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.DiscardDraftAsync(id, options, cancellationToken));

    public Task<T?> GetVersionAsync(Guid id, int number, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T?>(service => service.GetVersionAsync(id, number, options, cancellationToken));

    public Task<T> RestoreVersionAsync(Guid id, int number, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService<T>, T>(service => service.RestoreVersionAsync(id, number, options, cancellationToken));

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

    public Task<PagedResult<ContentDocument>> QueryTrashAsync(string type, ContentQuery query, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, PagedResult<ContentDocument>>(service => service.QueryTrashAsync(type, query, cancellationToken));

    public Task PurgeAsync(string type, Guid id, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService>(service => service.PurgeAsync(type, id, cancellationToken));

    public Task<PublishResult<ContentDocument>> PublishAsync(string type, Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, PublishResult<ContentDocument>>(service => service.PublishAsync(type, id, options, cancellationToken));

    public Task<ContentDocument> UnpublishAsync(string type, Guid id, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument>(service => service.UnpublishAsync(type, id, options, cancellationToken));

    public Task<ScheduleInfo> ScheduleAsync(string type, Guid id, ScheduleRequest request, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ScheduleInfo>(service => service.ScheduleAsync(type, id, request, options, cancellationToken));

    public Task CancelScheduleAsync(string type, Guid id, ScheduleAction action, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService>(service => service.CancelScheduleAsync(type, id, action, options, cancellationToken));

    public Task<ContentDocument> DiscardDraftAsync(string type, Guid id, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument>(service => service.DiscardDraftAsync(type, id, options, cancellationToken));

    public Task<IReadOnlyList<VersionInfo>> GetVersionsAsync(string type, Guid id, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, IReadOnlyList<VersionInfo>>(service => service.GetVersionsAsync(type, id, options, cancellationToken));

    public Task<ContentDocument?> GetVersionAsync(string type, Guid id, int number, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument?>(service => service.GetVersionAsync(type, id, number, options, cancellationToken));

    public Task<ContentDocument> RestoreVersionAsync(string type, Guid id, int number, LocaleWriteOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, ContentDocument>(service => service.RestoreVersionAsync(type, id, number, options, cancellationToken));

    public Task<IReadOnlyList<VersionChange>> DiffAsync(string type, Guid id, int fromVersion, int toVersion, LocaleOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, IReadOnlyList<VersionChange>>(service => service.DiffAsync(type, id, fromVersion, toVersion, options, cancellationToken));

    public Task<long> CountAsync(string type, CountOptions? options = null, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, long>(service => service.CountAsync(type, options, cancellationToken));

    public Task<BulkResult> BulkAsync(string type, BulkRequest request, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IContentService, BulkResult>(service => service.BulkAsync(type, request, cancellationToken));
}
