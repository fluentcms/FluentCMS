using FluentCMS.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Search.Tests;

// What the index stores and what a search looks for are folded alike, so that every provider sees the same words.
public sealed class SearchTextTests
{
    private static readonly SchemaType Articles = new SchemaBuilder().AddContentType<Article>().Build().FindContentType("article")!;

    [Theory]
    [InlineData("Hello, World!", "hello world")]
    [InlineData("  Café   crème  ", "cafe creme")]
    [InlineData("C# and .NET 10", "c and net 10")]
    [InlineData("foo-bar_baz", "foo bar baz")]
    [InlineData("ÀÉÎÕÜ ß Ø", "aeiou ß ø")]
    [InlineData("\"quoted\" 'words' * OR NOT", "quoted words or not")]
    [InlineData("!!!", "")]
    [InlineData("日本語 テキスト", "日本語 テキスト")]
    public void TextIsFoldedToWordsOfLettersAndDigits(string text, string folded) =>
        SearchText.Fold(text).ShouldBe(folded);

    [Fact]
    public void ASearchIsTheWordsOfItsText()
    {
        SearchText.Words("  Dot-Net  search ").ShouldBe(["dot", "net", "search"]);
        SearchText.Words("--").ShouldBeEmpty();
    }

    [Fact]
    public void AnEntryGivesTheTextOfItsFieldsWithRichTextAsPlainText()
    {
        var entry = new ContentDocument
        {
            ["title"] = "Hello",
            ["body"] = new RichText("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Ku"},{"type":"text","marks":[{"type":"bold"}],"text":"bernetes"}]},{"type":"paragraph","content":[{"type":"text","text":"rocks"}]}]}"""),
            ["tags"] = new List<object?> { "one", "two", 3 },
            ["views"] = 12L,
        };

        SearchText.Of(entry, [Field("title"), Field("body"), Field("tags"), Field("views"), Field("missing")]).ShouldBe("hello kubernetes rocks one two");
    }

    [Fact]
    public void ALongTextIsCutAtAWord()
    {
        var entry = new ContentDocument { ["body"] = string.Join(' ', Enumerable.Repeat("abcdefghi", 20_000)) };

        var text = SearchText.Of(entry, [Field("body")]);

        text.Length.ShouldBeLessThanOrEqualTo(SearchText.MaxLength);
        text.Split(' ').ShouldAllBe(word => word == "abcdefghi");
    }

    [Fact]
    public void OnlyFieldsMarkedSearchableAndNotHiddenAreRead()
    {
        SearchText.Fields(Articles).Select(field => field.Name).ShouldBe(["title", "body", "tagline"]);
    }

    // The fields read here only need a name.
    private static SchemaField Field(string name) =>
        new SchemaBuilder().AddCollection("other", type => type.Field(name, "text")).Build().FindContentType("other")!.FindField(name)!;
}
