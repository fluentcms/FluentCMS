using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Forms;

namespace FluentCMS.Tests.Admin;

public sealed class FactValuesTests
{
    private static readonly Guid Owner = Guid.Parse("0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e");

    [Fact]
    public void Posted_parts_become_the_fact_object_with_every_property()
    {
        var problems = new Dictionary<string, string>();

        var fact = FactValues.Read("price", JsonValue.Create(10m), Posted(
            ("price.kind", "url"),
            ("price.reference", " https://example.com "),
            ("price.owner", Owner.ToString()),
            ("price.validFrom", "2026-01-01T00:00")), problems);

        Assert.Empty(problems);
        Assert.Equal(
            $$"""{"value":10,"source":{"kind":"url","reference":"https://example.com"},"owner":"{{Owner}}","validFrom":"2026-01-01T00:00:00Z","validUntil":null,"lastVerified":null}""",
            fact!.ToJsonString());
    }

    [Fact]
    public void A_fact_without_any_details_is_unsourced_and_keeps_null_properties()
    {
        var fact = FactValues.Read("name", JsonValue.Create("Drill"), Posted(), []);

        Assert.Equal("""{"value":"Drill","source":null,"owner":null,"validFrom":null,"validUntil":null,"lastVerified":null}""", fact!.ToJsonString());
        Assert.True(FactValues.IsUnsourced(JsonDocument.Parse(fact.ToJsonString()).RootElement));
    }

    [Theory]
    [InlineData("price.kind", "url", "reference", "source")]
    [InlineData("price.reference", "x", "kind", "source")]
    [InlineData("price.kind", "rumour", "kind", "kind")]
    [InlineData("price.owner", "someone", "owner", "owner")]
    [InlineData("price.lastVerified", "yesterday", "lastVerified", "dateTime")]
    public void A_part_that_cannot_stand_is_a_problem_of_that_part(string key, string posted, string part, string problem)
    {
        var problems = new Dictionary<string, string>();

        Assert.Null(FactValues.Read("price", JsonValue.Create(10m), Posted((key, posted)), problems));
        Assert.Equal(problem, problems[part]);
    }

    [Fact]
    public void A_stored_fact_shows_its_value_and_parts()
    {
        var facts = new Dictionary<string, string?>();
        var stored = JsonDocument.Parse($$"""{"value": 10, "source": {"kind": "file", "reference": "prices.csv"}, "owner": "{{Owner}}", "validFrom": null, "validUntil": "2026-12-31T23:59:00+01:00", "lastVerified": null}""").RootElement;

        var value = FactValues.Show("price", stored, facts);

        Assert.Equal(10, value!.Value.GetInt32());
        Assert.Equal(("file", "prices.csv", Owner.ToString(), "2026-12-31T22:59"), (facts["price.kind"], facts["price.reference"], facts["price.owner"], facts["price.validUntil"]));
        Assert.False(FactValues.IsUnsourced(stored));
    }

    private static Dictionary<string, string?> Posted(params (string Key, string Value)[] parts)
    {
        return parts.ToDictionary(part => part.Key, part => (string?)part.Value);
    }
}
