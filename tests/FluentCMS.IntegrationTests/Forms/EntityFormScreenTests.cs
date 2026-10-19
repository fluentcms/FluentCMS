using System.Net;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Forms;

public sealed class EntityFormScreenTests
{
    [Fact]
    public async Task A_person_creates_an_entity_whose_facts_carry_their_source_owner_and_validity()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await ProductTypeAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        var form = await host.Client.GetStringAsync("/content/product/new");
        Assert.Contains("name=\"Form.Facts[price.kind]\"", form);
        Assert.Contains($"<option value=\"{ownerId}\">", form);

        var saved = await host.SubmitAsync("/content/product/new", "entry", new Dictionary<string, string>
        {
            ["Form.Values[name]"] = "Drill",
            ["Form.Values[price]"] = "129.5",
            ["Form.Facts[price.kind]"] = "url",
            ["Form.Facts[price.reference]"] = "https://example.com/catalog",
            ["Form.Facts[price.owner]"] = ownerId.ToString(),
            ["Form.Facts[price.validUntil]"] = "2026-12-31T23:59",
            ["Form.Facts[price.lastVerified]"] = "2026-10-18T09:00",
            ["Form.Rationale"] = "Add the drill.",
            ["Form.BaseVersion"] = "0",
        });

        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(Guid.Parse(host.LocationOf(saved)![6..42]))).Value);
        var operation = Assert.Single(set.Operations);
        Assert.Equal((OperationKind.Create, ItemKind.Entity), (operation.Kind, operation.ItemKind));
        var entity = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(operation.ItemId)).Value);
        var price = entity.Latest.Fields.GetProperty("price");
        Assert.Equal(129.5m, price.GetProperty("value").GetDecimal());
        Assert.Equal(("url", "https://example.com/catalog"), (price.GetProperty("source").GetProperty("kind").GetString(), price.GetProperty("source").GetProperty("reference").GetString()));
        Assert.Equal(ownerId.ToString(), price.GetProperty("owner").GetString());
        Assert.Equal("2026-12-31T23:59:00Z", price.GetProperty("validUntil").GetString());
        Assert.Equal("2026-10-18T09:00:00Z", price.GetProperty("lastVerified").GetString());
        Assert.Equal(JsonValueKind.Null, price.GetProperty("validFrom").ValueKind);

        // The name was saved without a source: it is stored, flagged unsourced, and the form says so.
        var unsourced = await host.AsUserAsync<IEntityFacts, IReadOnlyList<FactFlag>>(ownerId, async facts => (await facts.UnsourcedAsync("product")).Value);
        Assert.Equal(["name"], unsourced.Select(flag => flag.Field));
        var edit = await host.Client.GetStringAsync($"/content/product/{entity.Id}");
        Assert.Contains("value=\"https://example.com/catalog\"", edit);
        Assert.Contains("value=\"2026-12-31T23:59\"", edit);
        Assert.Contains("<option value=\"url\" selected", edit);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(edit, "data-testid=\"fact-unsourced\""));
    }

    [Fact]
    public async Task A_half_given_source_and_a_bad_time_are_reported_and_nothing_is_stored()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await ProductTypeAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        var refused = await host.SubmitAsync("/content/product/new", "entry", new Dictionary<string, string>
        {
            ["Form.Values[name]"] = "Drill",
            ["Form.Values[price]"] = "10",
            ["Form.Facts[name.kind]"] = "url",
            ["Form.Facts[price.validUntil]"] = "next year",
            ["Form.Rationale"] = "Add the drill.",
            ["Form.BaseVersion"] = "0",
        });
        var page = await refused.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Give both the kind of source and the source, or neither.", page);
        Assert.Contains("Enter a date and a time.", page);
        Assert.Contains("value=\"next year\"", page);
        Assert.Empty(await host.AsUserAsync<IEntryDirectory, IReadOnlyList<EntrySummary>>(ownerId, async entries => (await entries.ListAsync("product")).Value));
    }

    private static Task<ContentType> ProductTypeAsync(AdminHost host, Guid ownerId)
    {
        return host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(new NewContentType(
            "Product",
            "product",
            new ContentTypeDefinition([new FieldDefinition("name", "text", required: true), new FieldDefinition("price", "number")], isEntity: true)), TestChangeSetTargets.ContentModel)).Value);
    }
}
