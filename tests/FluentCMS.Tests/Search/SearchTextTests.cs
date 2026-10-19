using System.Text.Json;
using FluentCMS.Core.Search;

namespace FluentCMS.Tests.Search;

public sealed class SearchTextTests
{
    [Fact]
    public void An_entrys_text_is_every_string_and_number_in_its_fields_as_lowercase_words()
    {
        var fields = JsonDocument.Parse("""
            {
                "title": "Widget Pro",
                "sku": {"value": "A-4411", "source": {"kind": "url", "reference": "https://example.com/spec"}, "owner": null},
                "price": 19.99,
                "tags": ["Garden", "Café"],
                "active": true
            }
            """).RootElement;

        Assert.Equal("widget pro a 4411 url https example com spec 19 99 garden café", SearchText.From(fields));
    }

    // An id's parts would match queries such as "01" in every entry that references something.
    [Fact]
    public void A_string_that_is_a_guid_is_left_out()
    {
        var fields = JsonDocument.Parse("""
            {"title": "Episode 01", "product": "0199F3A1-7C2B-7D4E-8A1B-2C3D4E5F6A7B", "note": "0199f3a1 is not an id"}
            """).RootElement;

        Assert.Equal("episode 01 0199f3a1 is not an id", SearchText.From(fields));
    }

    [Fact]
    public void A_very_long_entry_is_cut_at_a_word_within_the_limit()
    {
        var fields = JsonSerializer.SerializeToElement(new { body = string.Join(' ', Enumerable.Repeat("lorem", 30_000)) });

        var text = SearchText.From(fields);

        Assert.True(text.Length <= SearchText.MaxLength);
        Assert.EndsWith("lorem", text);
    }

    [Theory]
    [InlineData("4411", new[] { "4411" })]
    [InlineData("  Widget  A-4411  widget ", new[] { "widget", "a", "4411" })]
    [InlineData("19.99 €", new[] { "19", "99" })]
    [InlineData("-- ?! ", new string[0])]
    [InlineData(null, new string[0])]
    public void A_query_is_split_into_the_same_words_as_the_text(string? query, string[] terms)
    {
        Assert.Equal(terms, SearchTerms.From(query));
    }
}
