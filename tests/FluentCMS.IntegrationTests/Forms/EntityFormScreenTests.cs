using System.Net;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Identity;
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
            ["Form.Facts[price.owner]"] = Guid.NewGuid().ToString(),
            ["Form.Rationale"] = "Add the drill.",
            ["Form.BaseVersion"] = "0",
        });
        var page = await refused.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Give both the kind of source and the source, or neither.", Fieldset(page, "name"));
        Assert.Contains("Enter a date and a time.", Fieldset(page, "price"));
        Assert.Contains("Choose an owner from the list.", Fieldset(page, "price"));
        Assert.Contains("value=\"next year\"", page);
        Assert.Empty(await host.AsUserAsync<IEntryDirectory, IReadOnlyList<EntrySummary>>(ownerId, async entries => (await entries.ListAsync("product")).Value));
    }

    [Fact]
    public async Task Fact_details_for_an_empty_value_are_reported_next_to_the_field()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await ProductTypeAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        var refused = await host.SubmitAsync("/content/product/new", "entry", new Dictionary<string, string>
        {
            ["Form.Values[name]"] = "Drill",
            ["Form.Facts[price.kind]"] = "file",
            ["Form.Facts[price.reference]"] = "prices.csv",
            ["Form.Rationale"] = "Add the drill.",
            ["Form.BaseVersion"] = "0",
        });

        Assert.Contains("Give the value too, or clear its fact details.", await refused.Content.ReadAsStringAsync());
        Assert.Empty(await host.AsUserAsync<IEntryDirectory, IReadOnlyList<EntrySummary>>(ownerId, async entries => (await entries.ListAsync("product")).Value));
    }

    [Fact]
    public async Task A_name_only_edit_changes_only_the_name_fact_and_keeps_every_other_fact_as_stored()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await ProductTypeAsync(host, ownerId);
        var leaverId = await host.AsUserAsync(ownerId, async users =>
        {
            var id = (await users.CreateAsync(new NewUser("leaver@example.com", "Leaver", "An0ther!Passw0rd"))).Value;
            await users.DisableAsync(id);
            return id;
        });

        // As an agent may store them: an owner who is no user, a source with its properties in another order and a trailing space,
        // an upper-case owner id, and times with seconds and offsets.
        var stranger = Guid.NewGuid();
        var created = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.CreateAsync(new NewEntry("product", Json($$"""
            {
                "name": {"value": "Drill", "source": null, "owner": "{{stranger}}", "validFrom": null, "validUntil": null, "lastVerified": null},
                "price": {"value": 4.50, "source": {"reference": "prices.csv ", "kind": "file"}, "owner": "{{leaverId.ToString().ToUpperInvariant()}}", "validFrom": "2026-01-01T00:00:00Z", "validUntil": "2026-12-31T23:59:59+01:00", "lastVerified": "2026-10-18T09:00:45.5+02:00"}
            }
            """)))).Value);
        var before = (await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(created.Id)).Value)).Latest.Fields;
        await host.SignInAsync(AdminHost.Owner);

        // The disabled owner and the owner who is no user stay selected, and the times show to the minute in UTC.
        var edit = await host.Client.GetStringAsync($"/content/product/{created.Id}");
        Assert.Contains($"<option value=\"{leaverId}\" selected>Leaver (disabled)</option>", Fieldset(edit, "price"));
        Assert.Contains($"<option value=\"{stranger}\" selected>Unknown user {stranger.ToString()[..8]}</option>", Fieldset(edit, "name"));
        Assert.Contains("value=\"2026-10-18T07:00\"", Fieldset(edit, "price"));
        Assert.Contains("value=\"2026-12-31T22:59\"", Fieldset(edit, "price"));
        Assert.Contains("data-testid=\"fact-unsourced\"", Fieldset(edit, "name"));
        Assert.DoesNotContain("data-testid=\"fact-unsourced\"", Fieldset(edit, "price"));

        // What the browser posts back: the widgets' text for every untouched part, and nothing for the unchecked box.
        var saved = await host.SubmitAsync($"/content/product/{created.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[name]"] = "Cordless drill",
            ["Form.Facts[name.owner]"] = stranger.ToString(),
            ["Form.Values[price]"] = "4.50",
            ["Form.Facts[price.kind]"] = "file",
            ["Form.Facts[price.reference]"] = "prices.csv ",
            ["Form.Facts[price.owner]"] = leaverId.ToString(),
            ["Form.Facts[price.validFrom]"] = "2026-01-01T00:00",
            ["Form.Facts[price.validUntil]"] = "2026-12-31T22:59",
            ["Form.Facts[price.lastVerified]"] = "2026-10-18T07:00",
            ["Form.Rationale"] = "Rename.",
            ["Form.BaseVersion"] = "1",
        });

        Assert.StartsWith("/sets/", host.LocationOf(saved));
        var fields = (await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(created.Id)).Value)).Latest.Fields;
        Assert.Equal(("Cordless drill", stranger.ToString()), (fields.GetProperty("name").GetProperty("value").GetString(), fields.GetProperty("name").GetProperty("owner").GetString()));
        Assert.Equal(JsonSerializer.Serialize(before.GetProperty("price")), JsonSerializer.Serialize(fields.GetProperty("price")));
        Assert.False(fields.TryGetProperty("inStock", out _));
    }

    // A type made an entity type after entries were written keeps their plain values until they are edited.
    [Fact]
    public async Task An_edit_of_an_entry_written_before_its_type_held_facts_saves_its_facts()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        IReadOnlyList<FieldDefinition> fields = [new FieldDefinition("name", "text", required: true), new FieldDefinition("price", "number")];
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(
            new NewContentType("Product", "product", new ContentTypeDefinition(fields)), TestChangeSetTargets.ContentModel)).Value);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("product", Json("""{"name": "Drill", "price": 4.5}""")))).Value);
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.ReviseAsync(
            "product", 1, new ContentTypeDefinition(fields, isEntity: true), TestChangeSetTargets.ContentModel)).Value);
        await host.SignInAsync(AdminHost.Owner);

        var saved = await host.SubmitAsync($"/content/product/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[name]"] = "Cordless drill",
            ["Form.Facts[name.owner]"] = ownerId.ToString(),
            ["Form.Values[price]"] = "4.5",
            ["Form.Rationale"] = "Record the facts.",
            ["Form.BaseVersion"] = "1",
        });

        Assert.StartsWith("/sets/", host.LocationOf(saved));
        var name = (await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(entry.Id)).Value)).Latest.Fields.GetProperty("name");
        Assert.Equal(("Cordless drill", ownerId.ToString()), (name.GetProperty("value").GetString(), name.GetProperty("owner").GetString()));
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    // The fact details fieldset of one field, where its problems and its unsourced marker are shown.
    private static string Fieldset(string page, string field)
    {
        var start = page.IndexOf($"data-fact=\"{field}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No fact details for {field}.");
        return page[start..page.IndexOf("</fieldset>", start, StringComparison.Ordinal)];
    }

    private static Task<ContentType> ProductTypeAsync(AdminHost host, Guid ownerId)
    {
        return host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(new NewContentType(
            "Product",
            "product",
            new ContentTypeDefinition(
                [new FieldDefinition("name", "text", required: true), new FieldDefinition("price", "number"), new FieldDefinition("inStock", "boolean")],
                isEntity: true)), TestChangeSetTargets.ContentModel)).Value);
    }
}
