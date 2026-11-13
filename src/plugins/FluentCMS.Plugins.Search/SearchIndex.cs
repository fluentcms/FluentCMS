using FluentCMS.Content;
using FluentCMS.Providers;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Search;

// The ISearchIndex slot. A slot is a singleton, so each search reads the index in a scope of its own. The engine has
// checked the query and the caller's right to read the type before it asks, and narrows the list to the ids found, so the
// index decides only which entries hold the words.
internal sealed class SearchIndex(IServiceScopeFactory scopes, SchemaModel schema, IPluginState plugins, IOperationContextAccessor operations) : ISearchIndex
{
    public async Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Where the plugin is disabled, a search is refused as it is in a host without an index.
        if ((request.Site ?? operations.Current?.Site) is { } site && !await plugins.IsEnabledAsync(SearchPlugin.Name, site, cancellationToken))
        {
            throw Refused(SearchErrorCodes.NoSearchIndex, "Searching needs a search index, and this site has none.");
        }

        if (schema.FindContentType(request.Type) is not { } type || SearchText.Fields(type).Count == 0)
        {
            throw Refused(SearchErrorCodes.NotSearchable, $"{request.Type} has no field marked [Searchable], so it cannot be searched.");
        }

        var words = SearchText.Words(request.Text);
        if (words.Count == 0 || request.Limit < 1)
        {
            return [];
        }

        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SearchStore>().MatchAsync(request, words, cancellationToken);
    }

    private static ValidationException Refused(string code, string message) => new([new ValidationError("q", code, message)]);
}
