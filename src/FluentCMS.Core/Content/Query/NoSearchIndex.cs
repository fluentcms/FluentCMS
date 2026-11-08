using FluentCMS.Providers;

namespace FluentCMS.Core.Content.Query;

// The default of the ISearchIndex slot: core has no index, so the engine refuses a search before it would ask this one.
internal sealed class NoSearchIndex : ISearchIndex
{
    public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"{QueryErrorCodes.NoSearchIndex}: no search index is registered. Add the search plugin or fill the ISearchIndex provider slot.");
}
