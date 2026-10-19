using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Forms;
using FluentCMS.Core.Schema;

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

    [Fact]
    public void Parts_that_show_as_the_stored_fact_keep_its_text()
    {
        var stored = JsonDocument.Parse($$"""{"value": 10, "source": null, "owner": "{{Owner.ToString().ToUpperInvariant()}}", "validFrom": null, "validUntil": null, "lastVerified": "2026-03-04T05:06:07.89+01:00"}""").RootElement;
        var shown = new Dictionary<string, string?>();
        FactValues.Show("price", stored, shown);
        var problems = new Dictionary<string, string>();

        var fact = FactValues.Read("price", JsonValue.Create(10), shown, problems, stored, new HashSet<Guid> { Owner });

        Assert.Empty(problems);
        Assert.Equal(Owner.ToString(), shown["price.owner"]);
        Assert.Equal(Owner.ToString().ToUpperInvariant(), fact![FactMetadata.Owner]!.GetValue<string>());
        Assert.Equal("2026-03-04T05:06:07.89+01:00", fact[FactMetadata.LastVerified]!.GetValue<string>());
    }

    [Fact]
    public void An_owner_who_is_not_a_known_user_is_a_problem()
    {
        var problems = new Dictionary<string, string>();

        Assert.Null(FactValues.Read("price", JsonValue.Create(10m), Posted(("price.owner", Guid.NewGuid().ToString())), problems, owners: new HashSet<Guid> { Owner }));
        Assert.Equal("owner", problems["owner"]);
    }

    [Theory]
    [InlineData("price.reference", "prices.csv", true)]
    [InlineData("price.validFrom", "2026-01-01T00:00", true)]
    [InlineData("other.kind", "url", false)]
    [InlineData("price.owner", " ", false)]
    public void Details_are_given_when_any_part_of_the_field_has_text(string key, string posted, bool expected)
    {
        Assert.Equal(expected, FactValues.HasDetails("price", Posted((key, posted))));
    }

    private static Dictionary<string, string?> Posted(params (string Key, string Value)[] parts)
    {
        return parts.ToDictionary(part => part.Key, part => (string?)part.Value);
    }
}
