using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Search;

// How many matches a search reads. The scope, deletions and filters are applied to the matches, so a search reads them a page at a
// time until it has enough hits, and stops at MaxMatches, which covers every entry at M1's 10,000 (SRC-01).
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    public int PageSize { get; set; } = 1_000;

    public int MaxMatches { get; set; } = 10_000;
}

internal sealed class SearchOptionsValidation : IValidateOptions<SearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SearchOptions options)
    {
        return options.PageSize >= 1 && options.MaxMatches >= options.PageSize
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Search__PageSize is {options.PageSize} and Search__MaxMatches is {options.MaxMatches}; the page size must be 1 or more and at most the maximum.");
    }
}
