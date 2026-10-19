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
