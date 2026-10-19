using System.Text.Json;
using FluentCMS.Core.Entities;

namespace FluentCMS.Tests.Entities;

public sealed class FactTests
{
    [Fact]
    public void The_facts_of_a_document_carry_their_metadata_and_flag_a_missing_source()
    {
        var owner = Guid.CreateVersion7();
        var fields = JsonSerializer.Deserialize<JsonElement>($$"""
            {
              "name": {"value": "Drill", "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {
                "value": 99,
                "source": {"kind": "url", "reference": "https://example.com/prices"},
                "owner": "{{owner}}",
                "validFrom": "2026-01-01T00:00:00Z",
                "validUntil": "2026-12-31T23:59:59+01:00",
                "lastVerified": "2026-10-01T08:00:00Z"
              }
            }
            """);

        var facts = Fact.ReadAll(fields);

        Assert.Equal(["name", "price"], facts.Select(fact => fact.Field));
        Assert.True(facts[0].IsUnsourced);
        Assert.Equal(new Fact("name", null, null, null, null, null, null), facts[0]);
        Assert.False(facts[1].IsUnsourced);
        Assert.Equal(
            new Fact(
                "price",
                "url",
                "https://example.com/prices",
                owner,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 31, 22, 59, 59, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero)),
            facts[1]);
    }
}
