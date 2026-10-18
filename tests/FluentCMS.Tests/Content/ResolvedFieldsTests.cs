using System.Text.Json;
using FluentCMS.Core.Content;
using static FluentCMS.Tests.Content.TestEntries;

namespace FluentCMS.Tests.Content;

public sealed class ResolvedFieldsTests
{
    [Fact]
    public void Values_land_at_their_pointers_in_a_copy_of_the_fields()
    {
        var fields = Json("""{"price": "id-1", "prices": ["id-1", "id-2"], "bundle": {"value": "id-3", "source": null}, "headline": "Hi"}""");

        var resolved = ResolvedFields.Replace(fields, new Dictionary<string, JsonElement?>
        {
            ["/price"] = Json("19.99"),
            ["/prices/1"] = null,
            ["/bundle/value"] = Json("""{"amount": 5}"""),
        });

        Assert.Equal(
            """{"price":19.99,"prices":["id-1",null],"bundle":{"value":{"amount":5},"source":null},"headline":"Hi"}""",
            resolved.GetRawText());
        Assert.Equal("id-1", fields.GetProperty("price").GetString());
    }

    [Fact]
    public void Pointer_segments_are_unescaped()
    {
        var resolved = ResolvedFields.Replace(Json("""{"a/b": {"c~d": 1}}"""), new Dictionary<string, JsonElement?> { ["/a~1b/c~0d"] = Json("2") });

        Assert.Equal("""{"a/b":{"c~d":2}}""", resolved.GetRawText());
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/prices/2")]
    [InlineData("/prices/x")]
    [InlineData("/headline/deeper")]
    public void A_pointer_that_leads_nowhere_is_a_broken_index(string pointer)
    {
        var fields = Json("""{"prices": ["id-1", "id-2"], "headline": "Hi"}""");

        Assert.Throws<InvalidOperationException>(() => ResolvedFields.Replace(fields, new Dictionary<string, JsonElement?> { [pointer] = null }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("price")]
    public void A_pointer_must_point_inside_the_fields(string pointer)
    {
        Assert.Throws<ArgumentException>(() => ResolvedFields.Replace(Json("""{"price": 1}"""), new Dictionary<string, JsonElement?> { [pointer] = null }));
    }
}
