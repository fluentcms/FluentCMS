using FluentCMS.Providers;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.GraphQL.Tests;

// The Blog host with a search index that finds as many ids as it is asked for when the text is "everything".
public sealed class SearchingBlogHost : BlogHost
{
    public SearchingBlogHost() => Host.Configure(cms => cms.Providers.Use<ISearchIndex, FullIndex>());

    public sealed class FullIndex : ISearchIndex
    {
        public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(request.Text == "everything" ? [.. Enumerable.Range(0, request.Limit).Select(_ => Guid.CreateVersion7())] : []);
    }
}

public sealed class GraphQLSearchTests(SearchingBlogHost blog) : IClassFixture<SearchingBlogHost>
{
    // As REST's searchTruncated (#3302).
    [Fact]
    public async Task AListSaysWhenItsSearchReachedTheMostMatchesOfTheIndex()
    {
        using var client = await blog.AdminAsync();

        var data = await client.QueryAsync("""{ cut: articleList(search: "everything") { searchTruncated } whole: articleList(search: "nothing") { searchTruncated } all: articleList { searchTruncated } }""");

        data["cut"]!["searchTruncated"]!.GetValue<bool>().ShouldBeTrue();
        data["whole"]!["searchTruncated"]!.GetValue<bool>().ShouldBeFalse();
        data["all"]!["searchTruncated"]!.GetValue<bool>().ShouldBeFalse();
    }
}
