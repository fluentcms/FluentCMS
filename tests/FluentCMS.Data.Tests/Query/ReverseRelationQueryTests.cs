using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

// article.author declares the reverse side author.articles, which is metadata only.
public sealed class ReverseRelationQueryTests : IDisposable
{
    private readonly QueryDatabase _database = new();

    public ReverseRelationQueryTests()
    {
        var author = _database.AddAuthor("Ann");
        var article = _database.AddArticle("Hello");
        _database.Relate("article", article, "author", "author", author);
    }

    public void Dispose() => _database.Dispose();

    private async Task<ValidationError> Rejected(string type, string queryString, IContentQueryAccess? access = null)
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync($"locale=en&{queryString}", access, type: type));
        return exception.Errors.Single();
    }

    [Theory]
    [InlineData("author", "filter[articles][eq]=x", "articles")]
    [InlineData("author", "filter[articles.title][eq]=x", "articles.title")]
    [InlineData("article", "filter[author.articles.title][eq]=x", "author.articles.title")]
    [InlineData("author", "fields=articles", "fields")]
    [InlineData("article", "fields=author.articles", "fields")]
    [InlineData("author", "populate=articles", "populate")]
    [InlineData("article", "populate=author.articles", "populate")]
    [InlineData("author", "sort=articles", "sort")]
    public async Task APathThatNamesAReverseSideIsRefusedAsMetadataOnly(string type, string queryString, string path)
    {
        var error = await Rejected(type, queryString);

        error.Code.ShouldBe(QueryErrorCodes.ReverseRelation);
        error.Path.ShouldBe(path);
        error.Message.ShouldContain("'articles' of author is the reverse side of article.author");
        error.Message.ShouldContain("metadata only");
    }

    [Fact]
    public async Task AFilterInsideALogicalNodeIsRefusedToo()
    {
        var query = new ContentQuery { Filter = new Or(new FieldFilter("name", FilterOperator.Equal, "Ann"), new FieldFilter("articles.title", FilterOperator.Equal, "x")) };

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.Engine().ExecuteAsync("author", query, TestContext.Current.CancellationToken));

        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(QueryErrorCodes.ReverseRelation);
    }

    [Theory]
    [InlineData("article.author")]
    [InlineData("article")]
    public async Task ACallerWhoCannotReadTheRelationFieldSeesAnUnknownField(string denied)
    {
        var access = new QueryAccess();
        if (denied.Contains('.', StringComparison.Ordinal))
        {
            access.DeniedFields.Add(denied);
        }
        else
        {
            access.DeniedTypes.Add(denied);
        }

        (await Rejected("author", "populate=articles", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
        (await Rejected("author", "filter[articles.title][eq]=x", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    [Fact]
    public async Task ANameThatIsNoReverseSideIsStillAnUnknownField() =>
        (await Rejected("author", "populate=posts")).Code.ShouldBe(QueryErrorCodes.UnknownField);
}
