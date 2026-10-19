using System.Net;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.IntegrationTests.Media;

namespace FluentCMS.IntegrationTests.Forms;

public sealed class EntryFormScreenTests
{
    [Fact]
    public async Task A_person_creates_an_entry_with_every_field_type_and_lands_on_its_open_set()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var (category, asset) = await KitchenSinkAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        var form = await host.Client.GetStringAsync("/content/kitchen-sink/new");
        Assert.Contains("name=\"Form.Values[title]\"", form);
        Assert.Contains("type=\"datetime-local\"", form);
        Assert.Contains("type=\"date\"", form);
        Assert.Contains($"<option value=\"{category}\"", form);
        Assert.Contains($"<option value=\"{asset}\">hero.jpg</option>", form);

        var saved = await host.SubmitAsync("/content/kitchen-sink/new", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Cordless drill",
            ["Form.Values[body]"] = "A **light** drill.",
            ["Form.Values[price]"] = "129.5",
            ["Form.Values[active]"] = "true",
            ["Form.Values[launch]"] = "2026-11-01T09:30",
            ["Form.Values[day]"] = "2026-11-02",
            ["Form.Values[colour]"] = "red",
            ["Form.Values[category]"] = category.ToString(),
            ["Form.Values[hero]"] = asset.ToString(),
            ["Form.Values[tags]"] = """["power", "tools"]""",
            ["Form.Values[extra]"] = """{"warranty": 2}""",
            ["Form.Rationale"] = "Add the drill for the launch.",
            ["Form.Sources"] = "https://example.com/catalog\nexports/drills.csv",
            ["Form.BaseVersion"] = "0",
        });

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var location = host.LocationOf(saved)!;
        Assert.Matches("^/sets/[0-9a-f-]{36}\\?done=saved$", location);
        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(Guid.Parse(location[6..42]))).Value);
        Assert.Equal((ChangeSetState.Open, "Add the drill for the launch."), (set.State, set.Rationale));
        Assert.Equal(["https://example.com/catalog", "exports/drills.csv"], set.Sources);
        var operation = Assert.Single(set.Operations);
        Assert.Equal((OperationKind.Create, ItemKind.Entry), (operation.Kind, operation.ItemKind));
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(operation.ItemId)).Value);
        var fields = entry.Latest.Fields;
        Assert.Equal(129.5m, fields.GetProperty("price").GetDecimal());
        Assert.True(fields.GetProperty("active").GetBoolean());
        Assert.Equal("2026-11-01T09:30:00Z", fields.GetProperty("launch").GetString());
        Assert.Equal("2026-11-02", fields.GetProperty("day").GetString());
        Assert.Equal(["power", "tools"], fields.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal(2, fields.GetProperty("extra").GetProperty("warranty").GetInt32());
        Assert.Contains("data-testid=\"review-done\"", await host.Client.GetStringAsync(location));

        // Editing shows the stored values, and the save opens a second set with the next version.
        var edit = await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}");
        Assert.Contains("value=\"2026-11-01T09:30\"", edit);
        Assert.Contains("value=\"129.5\"", edit);
        Assert.Contains("<option value=\"red\" selected", edit);
        var edited = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Cordless drill, 18 V",
            ["Form.Rationale"] = "Name the voltage.",
            ["Form.BaseVersion"] = "1",
        });
        var second = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(Guid.Parse(host.LocationOf(edited)![6..42]))).Value);
        Assert.Equal((OperationKind.Create, (int?)2), (Assert.Single(second.Operations).Kind, second.Operations[0].ProposedVersion));
    }

    [Fact]
    public async Task Problems_show_next_to_their_fields_and_nothing_is_stored()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await KitchenSinkAsync(host, ownerId);
        await host.SignInAsync(AdminHost.Owner);

        var refused = await host.SubmitAsync("/content/kitchen-sink/new", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "",
            ["Form.Values[price]"] = "cheap",
            ["Form.Values[launch]"] = "soon",
            ["Form.Values[colour]"] = "purple",
            ["Form.Values[extra]"] = "{",
            ["Form.Rationale"] = "",
            ["Form.BaseVersion"] = "0",
        });
        var page = await refused.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Fill in title.", page);
        Assert.Contains("Enter a number", page);
        Assert.Contains("Enter a date and a time.", page);
        Assert.Contains("Enter valid JSON", page);
        Assert.Contains("The value must be one of: red, blue.", page);
        Assert.Contains("Say why you are making this change.", page);
        Assert.Contains("value=\"cheap\"", page);
        Assert.True(System.Text.RegularExpressions.Regex.Count(page, "aria-invalid=\"true\"") >= 5);
        Assert.Empty((await host.AsUserAsync<IEntryDirectory, IReadOnlyList<EntrySummary>>(ownerId, async entries => (await entries.ListAsync("kitchen-sink")).Value)));
    }

    [Fact]
    public async Task A_form_from_an_older_version_is_refused_and_shows_the_entry_as_it_is_now()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await KitchenSinkAsync(host, ownerId);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("kitchen-sink", Json("""{"title": "First"}""")))).Value);
        await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.UpdateAsync(entry.Id, 1, Json("""{"title": "Second"}"""))).Value);
        await host.SignInAsync(AdminHost.Owner);

        var stale = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Mine",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "1",
        });
        var page = await stale.Content.ReadAsStringAsync();

        Assert.Contains("data-testid=\"entry-form-error\"", page);
        Assert.Contains("value=\"Second\"", page);
        Assert.Contains("data-version=\"2\"", page);
    }

    // A refused save comes back with the version the person started from, so their next save cannot overwrite a change made meanwhile.
    [Fact]
    public async Task A_form_refused_for_its_own_problems_keeps_its_base_version_and_its_next_save_is_a_conflict()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await KitchenSinkAsync(host, ownerId);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("kitchen-sink", Json("""{"title": "First"}""")))).Value);
        await host.SignInAsync(AdminHost.Owner);
        var form = await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}");
        Assert.Contains("name=\"Form.BaseVersion\" value=\"1\"", form);
        await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.UpdateAsync(entry.Id, 1, Json("""{"title": "Theirs"}"""))).Value);

        var refused = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Mine",
            ["Form.Values[extra]"] = "{",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "1",
        });
        Assert.Contains("name=\"Form.BaseVersion\" value=\"1\"", await refused.Content.ReadAsStringAsync());

        var again = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Mine",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "1",
        });

        Assert.Contains("Someone saved this entry since the form was shown.", await again.Content.ReadAsStringAsync());
        var stored = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(entry.Id)).Value);
        Assert.Equal((2, "Theirs"), (stored.LatestVersion, stored.Latest.Fields.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task An_edit_of_one_field_leaves_every_other_stored_value_exactly_as_it_was()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await KitchenSinkAsync(host, ownerId);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.CreateAsync(new NewEntry(
            "kitchen-sink",
            Json("""{"title": "Drill", "body": "\nFirst line.\nSecond line.", "subtitle": "Line one\nLine two", "launch": "2026-11-01T09:30:45.5+02:00", "price": 4.50}""")))).Value);
        await host.SignInAsync(AdminHost.Owner);

        // A text with line breaks gets a textarea, and a leading line break is doubled, since HTML drops the first.
        var form = await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}");
        Assert.Matches("<textarea[^>]*name=\"Form.Values\\[subtitle\\]\"[^>]*>Line one&#xA;Line two</textarea>", form);
        Assert.Matches("<textarea[^>]*name=\"Form.Values\\[body\\]\"[^>]*>&#xA;&#xA;First line.", form);

        // What a browser posts back for the untouched fields: the widgets' text, with CRLF line breaks and minutes only.
        var saved = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Cordless drill",
            ["Form.Values[body]"] = "\r\nFirst line.\r\nSecond line.",
            ["Form.Values[subtitle]"] = "Line one\r\nLine two",
            ["Form.Values[launch]"] = "2026-11-01T07:30",
            ["Form.Values[price]"] = "4.50",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "1",
        });

        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(Guid.Parse(host.LocationOf(saved)![6..42]))).Value);
        var fields = (await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(entry.Id)).Value)).Latest.Fields;
        Assert.Equal("Cordless drill", fields.GetProperty("title").GetString());
        Assert.Equal("\nFirst line.\nSecond line.", fields.GetProperty("body").GetString());
        Assert.Equal("Line one\nLine two", fields.GetProperty("subtitle").GetString());
        Assert.Equal("2026-11-01T09:30:45.5+02:00", fields.GetProperty("launch").GetString());
        Assert.Equal("4.50", fields.GetProperty("price").GetRawText());
        Assert.False(fields.TryGetProperty("active", out _));
        Assert.Equal(2, set.Operations[0].ProposedVersion);
    }

    [Fact]
    public async Task Editing_a_published_entry_proposes_an_update_from_the_published_version_and_a_stray_draft_is_named()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await KitchenSinkAsync(host, ownerId);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("kitchen-sink", Json("""{"title": "Published"}""")))).Value);
        await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets =>
        {
            var set = (await sets.OpenAsync(new NewChangeSet("Publish it.", []))).Value;
            await sets.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry.Id, null, 1).Value);
            await sets.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            await sets.TransitionAsync(set.Id, ChangeSetState.Approved);
            return (await sets.PublishAsync(set.Id)).Value;
        });

        // A draft no set reviews, as a rejected change leaves one.
        await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.UpdateAsync(entry.Id, 1, Json("""{"title": "Stray draft"}"""))).Value);
        await host.SignInAsync(AdminHost.Owner);

        var latest = await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}");
        Assert.Contains("data-testid=\"entry-draft-note\"", latest);
        Assert.Contains("value=\"Stray draft\"", latest);
        var published = await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}?from=published");
        Assert.Contains("value=\"Published\"", published);
        Assert.Contains("name=\"Form.BaseVersion\" value=\"2\"", published);

        var saved = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}?from=published", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Published, retitled",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "2",
        });

        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async sets => (await sets.GetAsync(Guid.Parse(host.LocationOf(saved)![6..42]))).Value);
        var operation = Assert.Single(set.Operations);
        Assert.Equal((OperationKind.Update, (int?)1, (int?)3), (operation.Kind, operation.BaseVersion, operation.ProposedVersion));

        // The saved set goes through the checks from the set view like any other.
        var page = await host.Client.GetStringAsync($"/sets/{set.Id}");
        var seen = System.Text.RegularExpressions.Regex.Match(page, "name=\"Submit.Seen\" value=\"([0-9a-f]+)\"").Groups[1].Value;
        var submitted = await host.SubmitAsync($"/sets/{set.Id}", "submit", new Dictionary<string, string> { ["Submit.Seen"] = seen });
        Assert.Contains("data-state=\"awaiting-review\"", await submitted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task After_a_revert_the_form_names_the_reverted_draft_and_a_form_from_a_published_version_since_replaced_is_refused()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await KitchenSinkAsync(host, ownerId);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("kitchen-sink", Json("""{"title": "First"}""")))).Value);
        await PublishAsync(host, ownerId, sets => sets.OpenAsync(new NewChangeSet("Publish it.", [])), ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry.Id, null, 1).Value);
        await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.UpdateAsync(entry.Id, 1, Json("""{"title": "Wrong"}"""))).Value);
        var wrong = await PublishAsync(host, ownerId, sets => sets.OpenAsync(new NewChangeSet("Retitle.", [])), ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, entry.Id, 1, 2).Value);
        await host.SignInAsync(AdminHost.Owner);

        // A form open on the published version when a revert publishes an older one can't put the reverted values back.
        Assert.Contains("name=\"Form.FromPublished\" value=\"2\"", await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}"));
        await PublishAsync(host, ownerId, sets => sets.RevertAsync(wrong, "The title was wrong."));
        var stale = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Wrong, retitled",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "2",
            ["Form.FromPublished"] = "2",
        });
        Assert.Contains("Someone saved this entry since the form was shown.", await stale.Content.ReadAsStringAsync());

        var reverted = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(entry.Id)).Value);
        Assert.Equal((EntryState.Reverted, (int?)1, 2), (reverted.State, reverted.PublishedVersion, reverted.LatestVersion));
        var latest = await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}");
        Assert.Contains("data-testid=\"entry-draft-note\"", latest);
        Assert.Contains("value=\"Wrong\"", latest);
        var published = await host.Client.GetStringAsync($"/content/kitchen-sink/{entry.Id}?from=published");
        Assert.Contains("value=\"First\"", published);
        Assert.Contains("name=\"Form.FromPublished\" value=\"1\"", published);

        // The published version moves on while the form is open: its values would undo that, so the save is refused.
        await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.UpdateAsync(entry.Id, 2, Json("""{"title": "Second"}"""))).Value);
        await PublishAsync(host, ownerId, sets => sets.OpenAsync(new NewChangeSet("Retitle again.", [])), ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, entry.Id, 1, 3).Value);
        var refused = await host.SubmitAsync($"/content/kitchen-sink/{entry.Id}?from=published", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "First, retitled",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "3",
            ["Form.FromPublished"] = "1",
        });

        Assert.Contains("Someone saved this entry since the form was shown.", await refused.Content.ReadAsStringAsync());
        Assert.Equal(3, (await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(entry.Id)).Value)).LatestVersion);

        // A reverted create leaves nothing published, and the form says whose values it shows.
        var created = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("kitchen-sink", Json("""{"title": "Never meant"}""")))).Value);
        var creation = await PublishAsync(host, ownerId, sets => sets.OpenAsync(new NewChangeSet("Add it.", [])), ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, created.Id, null, 1).Value);
        await PublishAsync(host, ownerId, sets => sets.RevertAsync(creation, "It was never meant."));
        var revertedCreate = await host.Client.GetStringAsync($"/content/kitchen-sink/{created.Id}");
        Assert.Contains("whose creation was reverted", revertedCreate);
        Assert.DoesNotContain("data-testid=\"from-published\"", revertedCreate);
    }

    // A field whose type changed keeps its stored value until it is saved; the form then saves what the new type takes.
    [Fact]
    public async Task An_untouched_value_its_field_type_no_longer_takes_is_saved_as_the_new_type()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        FieldDefinition[] before = [new FieldDefinition("title", "text"), new FieldDefinition("code", "number")];
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(
            new NewContentType("Part", "part", new ContentTypeDefinition(before)), TestChangeSetTargets.ContentModel)).Value);
        var entry = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("part", Json("""{"title": "Bolt", "code": 45}""")))).Value);
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.ReviseAsync(
            "part", 1, new ContentTypeDefinition([new FieldDefinition("title", "text"), new FieldDefinition("code", "text")]), TestChangeSetTargets.ContentModel)).Value);
        await host.SignInAsync(AdminHost.Owner);

        var saved = await host.SubmitAsync($"/content/part/{entry.Id}", "entry", new Dictionary<string, string>
        {
            ["Form.Values[title]"] = "Hex bolt",
            ["Form.Values[code]"] = "45",
            ["Form.Rationale"] = "Retitle.",
            ["Form.BaseVersion"] = "1",
        });

        Assert.StartsWith("/sets/", host.LocationOf(saved));
        var code = (await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.GetAsync(entry.Id)).Value)).Latest.Fields.GetProperty("code");
        Assert.Equal((JsonValueKind.String, "45"), (code.ValueKind, code.GetString()));
    }

    [Fact]
    public async Task The_content_pages_list_types_and_entries_and_need_a_signed_in_person()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await KitchenSinkAsync(host, ownerId);

        Assert.StartsWith("/sign-in?ReturnUrl=", host.LocationOf(await host.Client.GetAsync("/content/kitchen-sink/new")));
        await host.SignInAsync(AdminHost.Owner);

        Assert.Contains("data-type=\"kitchen-sink\"", await host.Client.GetStringAsync("/content"));
        var entries = await host.Client.GetStringAsync("/content/category");
        Assert.Contains("data-entry=", entries);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/content/nothing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/content/kitchen-sink/{Guid.NewGuid()}")).StatusCode);
    }

    // Opens a set, adds the operations and publishes it; a revert opens its own set with its operations.
    private static Task<Guid> PublishAsync(AdminHost host, Guid ownerId, Func<IChangeSetDirectory, Task<FluentCMS.Core.Results.Result<ChangeSet>>> open, params ChangeOperation[] operations)
    {
        return host.AsUserAsync<IChangeSetDirectory, Guid>(ownerId, async sets =>
        {
            var set = (await open(sets)).Value;
            foreach (var operation in operations)
            {
                await sets.AddOperationAsync(set.Id, operation);
            }

            await sets.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            await sets.TransitionAsync(set.Id, ChangeSetState.Approved);
            return (await sets.PublishAsync(set.Id)).Value.Id;
        });
    }

    // A type with one field of every built-in type, a category entry to reference and an asset to show.
    private static async Task<(Guid Category, Guid Asset)> KitchenSinkAsync(AdminHost host, Guid ownerId)
    {
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(new NewContentType(
            "Category", "category", new ContentTypeDefinition([new FieldDefinition("name", "text", required: true)])), TestChangeSetTargets.ContentModel)).Value);
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(new NewContentType(
            "Kitchen sink",
            "kitchen-sink",
            new ContentTypeDefinition(
            [
                new FieldDefinition("title", "text", required: true, settings: Json("""{"maxLength": 80}""")),
                new FieldDefinition("body", "richText"),
                new FieldDefinition("subtitle", "text"),
                new FieldDefinition("price", "number"),
                new FieldDefinition("active", "boolean"),
                new FieldDefinition("launch", "dateTime"),
                new FieldDefinition("day", "dateTime", settings: Json("""{"dateOnly": true}""")),
                new FieldDefinition("colour", "enum", settings: Json("""{"values": ["red", "blue"]}""")),
                new FieldDefinition("category", "reference", settings: Json("""{"target": "category"}""")),
                new FieldDefinition("hero", "media"),
                new FieldDefinition("tags", "list", settings: Json("""{"itemType": "text"}""")),
                new FieldDefinition("extra", "json"),
            ])), TestChangeSetTargets.ContentModel)).Value);
        var category = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries =>
            (await entries.CreateAsync(new NewEntry("category", Json("""{"name": "Tools"}""")))).Value);
        var asset = await host.AsUserAsync<IMediaLibrary, MediaAsset>(ownerId, async library => (await library.UploadAsync(new NewAsset(
            new MemoryStream(MediaFiles.Jpeg()), "image/jpeg", AssetMetadata.Create("hero.jpg", null, null, "A drill.", null).Value))).Value);
        return (category.Id, asset.Id);
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
