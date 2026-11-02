using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

public sealed class CompiledSqlTests : IDisposable
{
    private readonly QueryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    // The SQL without the parameter values that ToQueryString lists above it.
    private string Sql(string queryString, IContentQueryAccess? access = null)
    {
        var text = _database.Engine(access).Compile("article", BracketQueryParser.Parse(queryString)).Rows.ToQueryString();
        return string.Join('\n', text.Split('\n').Where(line => !line.StartsWith(".param", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("filter[title][eq]=needle", "needle")]
    [InlineData("filter[title][contains]=needle", "needle")]
    [InlineData("filter[title][startsWith]=needle", "needle")]
    [InlineData("filter[seo.metaTitle][eq]=needle", "needle")]
    [InlineData("filter[publishedOn][gt]=2031-05-06T07:08:09Z", "2031")]
    [InlineData("filter[issued][lt]=2031-05-06", "2031")]
    [InlineData("filter[id][eq]=0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f", "0192f2a4")]
    [InlineData("filter[author][in]=0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f", "0192f2a4")]
    [InlineData("filter[metadata.minutes][eq]=987654", "987654")]
    [InlineData("filter[labels][in]=net,sql", "'sql'")]
    [InlineData("filter[title][in]=needle,other", "needle")]
    [InlineData("filter[title][nin]=needle,other", "needle")]
    [InlineData("filter[views][gt]=987654", "987654")]
    [InlineData("filter[views][between]=987654,987655", "987654")]
    [InlineData("filter[price][lt]=98.76", "98.76")]
    [InlineData("filter[author.name][eq]=needle", "needle")]
    [InlineData("filter[sections][hasBlock]=hero", "'hero'")]
    [InlineData("filter[sections.hero.heading][eq]=needle", "needle")]
    [InlineData("filter[metadata.audience][eq]=needle", "needle")]
    [InlineData("filter[metadata.audience][contains]=needle", "needle")]
    [InlineData("filter[labels][contains]=net", "'net'")]
    [InlineData("filter[title][eq]=x' OR 1=1 --", "OR 1=1")]
    public void EveryUserValueIsBoundAsAParameter(string filter, string value)
    {
        var sql = Sql($"{filter}&locale=needle-locale");

        sql.ShouldNotContain(value, Case.Sensitive);
        sql.ShouldContain("@");
        sql.ShouldNotContain("needle-locale");
    }

    [Fact]
    public void ACursorValueIsBoundAsAParameter()
    {
        var cursor = System.Buffers.Text.Base64Url.EncodeToString("""{"title":"needle' OR 1=1 --","id":"0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f"}"""u8);

        var sql = Sql($"sort=title&locale=en&cursor={cursor}");

        sql.ShouldNotContain("needle");
        sql.ShouldNotContain("0192f2a4");
    }

    [Fact]
    public void AnInjectionAttemptFindsNothing()
    {
        _database.AddArticle("Alpha");

        var count = _database.Engine().Compile("article", BracketQueryParser.Parse("filter[title][eq]=x' OR 1=1 --&locale=en")).Rows.Count();

        count.ShouldBe(0);
    }

    [Theory]
    [InlineData("status=published", "\"state\" = 1")]
    [InlineData("status=draft", "\"state\" = 0")]
    public void TheStateIsInlinedAsALiteral(string status, string expected)
    {
        var sql = Sql($"{status}&locale=en&filter[author.name][eq]=Ann", new QueryAccess());

        sql.ShouldContain(expected);
        sql.ShouldNotContain("@state");
    }

    [Fact]
    public void ARelationPathIsANestedExistsThroughTheReferenceTable()
    {
        var sql = Sql("filter[author.name][eq]=Ann&locale=en");

        sql.ShouldContain("EXISTS");
        sql.ShouldContain("\"cms_reference\"");
        sql.ShouldContain("\"cms_author\"");
        sql.ShouldContain("\"source_type\" = 'article'");
        sql.ShouldContain("\"field\" = 'author'");
    }

    [Fact]
    public void ARowRuleIsPartOfTheOneQuery()
    {
        var access = new QueryAccess();
        access.RowRules["article"] = new FieldFilter("createdBy", FilterOperator.Equal, Guid.CreateVersion7());

        var sql = Sql("filter[views][gt]=1&locale=en", access);

        sql.ShouldContain("\"created_by\" = @");
        sql.ShouldContain("\"views\" > @");
    }

    [Fact]
    public void ASelectionReadsOnlyTheColumnsItNeeds()
    {
        var sql = Sql("fields=title&sort=views&locale=en");

        sql.ShouldContain("\"title\"");
        sql.ShouldContain("\"views\"");
        sql.ShouldNotContain("\"summary\"");
        sql.ShouldNotContain("\"secret\"");
    }

    [Fact]
    public void AHiddenFieldIsNeverRead() =>
        Sql("locale=en").ShouldNotContain("\"secret\"");
}
