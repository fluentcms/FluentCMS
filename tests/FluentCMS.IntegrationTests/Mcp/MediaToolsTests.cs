using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class MediaToolsTests
{
    private static readonly string Image = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4]);

    // S2 in the F-MED acceptance, as far as media goes: the image and the page that shows it arrive in one set.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_uploads_an_image_and_attaches_it_to_a_page_in_one_set(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, types: ["product-page"]);
        var page = await PageAsync(agent);

        var uploaded = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["folder"] = "products/drills",
            ["tags"] = new[] { "product-shot" },
            ["session_id"] = "launch",
            ["rationale"] = "Show the drill on its page.",
        });
        var assetId = (string)uploaded["media"]!["id"]!;
        var attached = await agent.CallAsync("attach_media", new()
        {
            ["entry_id"] = page.ToString(),
            ["base_version"] = 1,
            ["field"] = "hero",
            ["asset_id"] = assetId,
            ["session_id"] = "launch",
            ["rationale"] = "Ignored once the set is open.",
        });

        var setId = (string)attached["changeSet"]!["id"]!;
        Assert.Equal(setId, (string?)uploaded["changeSet"]!["id"]);
        Assert.Equal(
            [("create", "media"), ("update", "entry")],
            attached["changeSet"]!["operations"]!.AsArray().Select(operation => ((string)operation!["kind"]!, (string)operation["itemKind"]!)));
        Assert.Equal(assetId, (string?)attached["entry"]!["fields"]!["hero"]);
        Assert.Null((string?)uploaded["media"]!["altText"]);

        await agent.CallAsync("submit_change_set", new() { ["id"] = setId });
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(Guid.Parse(setId), ChangeSetState.Approved));
        var published = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.PublishAsync(Guid.Parse(setId)));

        Assert.Equal(agent.Credential.Initiator, published.Initiator);
        Assert.Equal(1, await agent.QueryAsync(context => context.MediaAssets.Select(row => row.PublishedVersion).SingleAsync()));
        Assert.Equal(2, await agent.QueryAsync(context => context.Entries.Where(row => row.Id == page).Select(row => row.PublishedVersion).SingleAsync()));

        var listed = await agent.CallAsync("list_media", new() { ["folder"] = "products", ["include_subfolders"] = true });
        Assert.Equal(assetId, (string?)Assert.Single(listed["media"]!.AsArray())!["id"]);
        Assert.False((bool)listed["media"]![0]!["hasAltText"]!);
    }

    // S2 in one set (MED-02): the agent uploads an image without alt text and attaches it to a page, the check names the field, the
    // agent adds alt text to the image in the same set, and the set passes the check it runs when submitted.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_uploads_attaches_and_describes_an_image_in_one_set_until_the_check_passes(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var page = await PageAsync(agent);
        await ActiveAltTextCheckAsync(agent);

        var uploaded = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "launch",
            ["rationale"] = "Show the drill on its page.",
        });
        var assetId = (string)uploaded["media"]!["id"]!;
        var setId = (string)uploaded["changeSet"]!["id"]!;
        await agent.CallAsync("attach_media", new()
        {
            ["entry_id"] = page.ToString(),
            ["base_version"] = 1,
            ["field"] = "hero",
            ["asset_id"] = assetId,
            ["session_id"] = "launch",
            ["rationale"] = "Ignored once the set is open.",
        });
        Dictionary<string, object?> Draft() => new()
        {
            ["content_type"] = "product-page",
            ["fields"] = JsonNode.Parse($$"""{"headline": "The Widget", "hero": "{{assetId}}"}"""),
            ["id"] = page.ToString(),
            ["change_set_id"] = setId,
        };

        var before = await agent.CallAsync("check_draft", Draft());
        var described = await agent.CallAsync("update_media", new()
        {
            ["id"] = assetId,
            ["base_version"] = 1,
            ["alt_text"] = "A cordless drill on a workbench.",
            ["session_id"] = "launch",
            ["rationale"] = "Describe the drill for people who cannot see it.",
        });
        var after = await agent.CallAsync("check_draft", Draft());
        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = setId });

        Assert.Equal("fail", (string?)before["status"]);
        Assert.Equal("/hero", (string?)before["results"]![0]!["findings"]![0]!["pointer"]);
        var media = described["changeSet"]!["operations"]!.AsArray().Single(operation => (string?)operation!["itemKind"] == "media")!;
        Assert.Equal(("create", 2), ((string)media["kind"]!, (int)media["proposedVersion"]!));
        Assert.Equal("pass", (string?)after["status"]);
        var altText = submitted["checks"]!["items"]!.AsArray().Single()!["results"]!.AsArray().Single(result => (string?)result!["check"] == "alt-text")!;
        Assert.Equal("pass", (string?)altText["status"]);
    }

    // The same for an entity's media fact: the photo of a product passes once update_media gives it alt text in the product's set.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_entity_s_media_fact_passes_once_update_media_describes_it_in_the_same_set(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(
            new NewContentType("Product", "product", new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("photo", "media")], isEntity: true)),
            TestChangeSetTargets.ContentModel));
        var check = new CheckDefinition(
            "altText",
            "Every image a product shows has alt text.",
            JsonSerializer.Deserialize<JsonElement>("{}"),
            [
                new CheckExample("described", CheckVerdict.Pass, JsonSerializer.Deserialize<JsonElement>("""{"name": {"value": "A drill"}}"""), contentType: "product"),
                new CheckExample("bare", CheckVerdict.Fail, JsonSerializer.Deserialize<JsonElement>("""{"name": {"value": "![](/drill.jpg)"}}"""), contentType: "product"),
            ],
            scope: new CheckScope(["product"]));
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.CreateAsync(new NewCheck("alt-text", check)));
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.ActivateAsync("alt-text", 1));

        var uploaded = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "catalog",
            ["rationale"] = "A photo for the drill.",
        });
        var assetId = (string)uploaded["media"]!["id"]!;
        var setId = (string)uploaded["changeSet"]!["id"]!;
        Dictionary<string, object?> Draft() => new()
        {
            ["content_type"] = "product",
            ["fields"] = JsonNode.Parse($$"""
                {
                  "name": {"value": "Drill", "source": {"kind": "system", "reference": "seed"}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
                  "photo": {"value": "{{assetId}}", "source": {"kind": "system", "reference": "seed"}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}
                }
                """),
            ["change_set_id"] = setId,
        };

        var before = await agent.CallAsync("check_draft", Draft());
        await agent.CallAsync("update_media", new()
        {
            ["id"] = assetId,
            ["base_version"] = 1,
            ["alt_text"] = "A cordless drill on a workbench.",
            ["session_id"] = "catalog",
            ["rationale"] = "Describe the photo.",
        });
        var after = await agent.CallAsync("check_draft", Draft());

        Assert.Equal("/photo/value", (string?)before["results"]![0]!["findings"]![0]!["pointer"]);
        Assert.Equal("pass", (string?)after["status"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Check_draft_refuses_a_change_set_it_cannot_read_or_that_has_ended(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await PageAsync(agent);
        var opened = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "rejected",
            ["rationale"] = "A set to reject.",
        });
        var rejectedId = Guid.Parse((string)opened["changeSet"]!["id"]!);
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(rejectedId, ChangeSetState.ChecksRunning));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(rejectedId, ChangeSetState.AwaitingReview));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.RejectAsync(rejectedId, "Not now."));
        Dictionary<string, object?> Draft(Guid changeSetId) => new()
        {
            ["content_type"] = "product-page",
            ["fields"] = JsonNode.Parse("""{"headline": "The Widget"}"""),
            ["change_set_id"] = changeSetId.ToString(),
        };

        var unknown = await agent.FailAsync("check_draft", Draft(Guid.CreateVersion7()));
        var ended = await agent.FailAsync("check_draft", Draft(rejectedId));

        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Code(unknown));
        Assert.Equal("argument_invalid", Code(ended));
        Assert.Contains("rejected", (string?)ended["errors"]![0]!["message"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_alt_text_check_passes_once_the_pages_set_adds_alt_text_with_update_media(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var page = await PageAsync(agent);
        var uploaded = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "library",
            ["rationale"] = "An image for the catalog.",
        });
        var assetId = (string)uploaded["media"]!["id"]!;
        await PublishAsPersonAsync(agent, (string)uploaded["changeSet"]!["id"]!);
        await ActiveAltTextCheckAsync(agent);

        var attached = await agent.CallAsync("attach_media", new()
        {
            ["entry_id"] = page.ToString(),
            ["base_version"] = 1,
            ["field"] = "hero",
            ["asset_id"] = assetId,
            ["session_id"] = "launch",
            ["rationale"] = "Show the drill on its page.",
        });
        var setId = (string)attached["changeSet"]!["id"]!;
        Dictionary<string, object?> Draft() => new()
        {
            ["content_type"] = "product-page",
            ["fields"] = JsonNode.Parse($$"""{"headline": "The Widget", "hero": "{{assetId}}"}"""),
            ["id"] = page.ToString(),
            ["change_set_id"] = setId,
        };

        var before = await agent.CallAsync("check_draft", Draft());
        await agent.CallAsync("update_media", new()
        {
            ["id"] = assetId,
            ["base_version"] = 1,
            ["alt_text"] = "A cordless drill on a workbench.",
            ["session_id"] = "launch",
            ["rationale"] = "Describe the drill for people who cannot see it.",
        });
        var after = await agent.CallAsync("check_draft", Draft());

        Assert.Equal("fail", (string?)before["status"]);
        Assert.Equal("/hero", (string?)before["results"]![0]!["findings"]![0]!["pointer"]);
        Assert.Equal("pass", (string?)after["status"]);

        // Submitted, the set's own checks read the version it proposes too.
        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = setId });
        var altText = submitted["checks"]!["items"]!.AsArray().Single()!["results"]!.AsArray().Single(result => (string?)result!["check"] == "alt-text")!;
        Assert.Equal("pass", (string?)altText["status"]);
    }

    // A version a set moved past within the set is still the set's, so outside it the alt text never counts, also once the set is
    // rejected: readers keep seeing the published version without alt text, or no version at all.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Versions_a_set_moved_past_do_not_count_outside_it_while_pending_or_once_rejected(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await PageAsync(agent);
        await ActiveAltTextCheckAsync(agent);
        var library = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "library",
            ["rationale"] = "An image for the catalog.",
        });
        var published = (string)library["media"]!["id"]!;
        await PublishAsPersonAsync(agent, (string)library["changeSet"]!["id"]!);

        var fresh = (string)(await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "saw.png",
            ["alt_text"] = "A circular saw.",
            ["session_id"] = "describe",
            ["rationale"] = "A new image.",
        }))["media"]!["id"]!;
        var described = await agent.CallAsync("update_media", new()
        {
            ["id"] = published,
            ["base_version"] = 1,
            ["alt_text"] = "A cordless drill on a workbench.",
            ["session_id"] = "describe",
            ["rationale"] = "Describe the drill.",
        });
        foreach (var (id, baseVersion) in new[] { (published, 2), (fresh, 1) })
        {
            await agent.CallAsync("update_media", new()
            {
                ["id"] = id,
                ["base_version"] = baseVersion,
                ["caption"] = "From the spring catalog.",
                ["session_id"] = "describe",
                ["rationale"] = "Caption the image.",
            });
        }

        var setId = Guid.Parse((string)described["changeSet"]!["id"]!);
        async Task<JsonObject> CheckAsync(string assetId, Guid? changeSetId = null)
        {
            var draft = new Dictionary<string, object?>
            {
                ["content_type"] = "product-page",
                ["fields"] = JsonNode.Parse($$"""{"headline": "The Widget", "hero": "{{assetId}}"}"""),
            };
            if (changeSetId is { } id)
            {
                draft["change_set_id"] = id.ToString();
            }

            return await agent.CallAsync("check_draft", draft);
        }

        string? Finding(JsonObject result) => (string?)result["results"]![0]!["findings"]![0]!["message"];

        var pending = await CheckAsync(published);
        var pendingFresh = await CheckAsync(fresh);
        var inTheSet = await CheckAsync(published, setId);
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(setId, ChangeSetState.ChecksRunning));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(setId, ChangeSetState.AwaitingReview));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.RejectAsync(setId, "Not now."));
        var rejected = await CheckAsync(published);
        var rejectedFresh = await CheckAsync(fresh);

        Assert.Equal("Add alt text to the image 'drill.png' in hero that says what it shows; set it with update_media.", Finding(pending));
        Assert.StartsWith("The image 'saw.png' in hero is not published", Finding(pendingFresh));
        Assert.Equal("pass", (string?)inTheSet["status"]);
        Assert.Equal(Finding(pending), Finding(rejected));
        Assert.Equal(Finding(pendingFresh), Finding(rejectedFresh));
    }

    // A person who describes an image in the library while an agent's set holds it changes what goes live with that set, at once.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_person_s_alt_text_in_the_library_counts_for_the_set_that_holds_the_image(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var page = await PageAsync(agent);
        await ActiveAltTextCheckAsync(agent);
        var uploaded = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "launch",
            ["rationale"] = "Show the drill on its page.",
        });
        var assetId = (string)uploaded["media"]!["id"]!;
        var setId = (string)uploaded["changeSet"]!["id"]!;
        await agent.CallAsync("attach_media", new()
        {
            ["entry_id"] = page.ToString(),
            ["base_version"] = 1,
            ["field"] = "hero",
            ["asset_id"] = assetId,
            ["session_id"] = "launch",
            ["rationale"] = "Ignored once the set is open.",
        });
        async Task<string?> StatusAsync(string? changeSetId)
        {
            var draft = new Dictionary<string, object?>
            {
                ["content_type"] = "product-page",
                ["fields"] = JsonNode.Parse($$"""{"headline": "The Widget", "hero": "{{assetId}}"}"""),
            };
            if (changeSetId is not null)
            {
                draft["change_set_id"] = changeSetId;
            }

            return (string?)(await agent.CallAsync("check_draft", draft))["status"];
        }


        var before = await StatusAsync(setId);
        await agent.AsPersonAsync<IMediaLibrary, MediaAsset>(media =>
            media.UpdateAsync(Guid.Parse(assetId), 1, AssetMetadata.Create("drill.png", null, [], "A cordless drill on a workbench.", null).Value));
        var after = await StatusAsync(setId);
        await PublishAsPersonAsync(agent, setId);
        var live = await StatusAsync(null);

        Assert.Equal(("fail", "pass", "pass"), (before, after, live));
        Assert.Equal(1, await agent.QueryAsync(context => context.MediaAssets.Select(row => row.PublishedVersion).SingleAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Alt_text_added_later_is_in_the_proposed_version_and_the_other_metadata_stays(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var uploaded = await agent.CallAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["tags"] = new[] { "product-shot" },
            ["session_id"] = "upload",
            ["rationale"] = "Upload.",
        });
        var assetId = (string)uploaded["media"]!["id"]!;

        var described = await agent.CallAsync("update_media", new()
        {
            ["id"] = assetId,
            ["base_version"] = 1,
            ["alt_text"] = "A cordless drill on a workbench.",
            ["session_id"] = "describe",
            ["rationale"] = "Describe the drill for people who cannot see it.",
        });
        var diff = await agent.CallAsync("get_change_set_diff", new() { ["id"] = (string)described["changeSet"]!["id"]! });
        var media = await agent.CallAsync("get_media", new() { ["id"] = assetId });

        var operation = Assert.Single(described["changeSet"]!["operations"]!.AsArray())!;
        Assert.Equal(("create", 2), ((string)operation["kind"]!, (int)operation["proposedVersion"]!));
        // Never published, so the set proposes the whole asset, now with its alt text.
        var altText = Assert.Single(diff["items"]![0]!["fields"]!.AsArray(), field => (string?)field!["name"] == "altText")!;
        Assert.Equal(("added", "A cordless drill on a workbench."), ((string)altText["change"]!, (string)altText["after"]!));
        Assert.Equal(("drill.png", "product-shot"), ((string)media["fileName"]!, (string)media["tags"]![0]!));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_upload_over_the_limit_or_unlike_its_type_is_refused_and_stores_nothing(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: new Dictionary<string, string?> { ["Media:MaxBytes"] = "8" });

        var large = await agent.FailAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "upload",
            ["rationale"] = "Upload.",
        });
        var disguised = await agent.FailAsync("upload_media", new()
        {
            ["content_base64"] = Convert.ToBase64String("<html>"u8),
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "upload",
            ["rationale"] = "Upload.",
        });

        Assert.Equal(MediaErrorCodes.MediaTooLarge, Code(large));
        Assert.Equal(MediaErrorCodes.ContentMismatch, Code(disguised));
        Assert.Equal(0, await agent.QueryAsync(context => context.MediaAssets.CountAsync()));
        Assert.Equal(0, await agent.QueryAsync(context => context.ChangeSets.CountAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Refused_uploads_and_attachments_say_why_and_store_nothing(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, operations: [AgentOperation.Read, AgentOperation.Update]);
        var page = await PageAsync(agent);

        var noCreate = await agent.FailAsync("upload_media", new()
        {
            ["content_base64"] = Image,
            ["content_type"] = "image/png",
            ["file_name"] = "drill.png",
            ["session_id"] = "upload",
            ["rationale"] = "Upload.",
        });
        var notBase64 = await agent.FailAsync("upload_media", new()
        {
            ["content_base64"] = "not base64!",
            ["content_type"] = "image/png",
            ["file_name"] = "a/b.png",
            ["session_id"] = "upload",
            ["rationale"] = "Upload.",
        });
        var notMedia = await agent.FailAsync("attach_media", new()
        {
            ["entry_id"] = page.ToString(),
            ["base_version"] = 1,
            ["field"] = "headline",
            ["asset_id"] = Guid.NewGuid().ToString(),
            ["session_id"] = "attach",
            ["rationale"] = "Attach.",
        });
        var missingAsset = await agent.FailAsync("attach_media", new()
        {
            ["entry_id"] = page.ToString(),
            ["base_version"] = 1,
            ["field"] = "hero",
            ["asset_id"] = Guid.NewGuid().ToString(),
            ["session_id"] = "attach",
            ["rationale"] = "Attach.",
        });

        Assert.Equal(AgentPermissionErrorCodes.ScopeDenied, Code(noCreate));
        Assert.Equal(["argument_invalid", "file_name_invalid"], notBase64["errors"]!.AsArray().Select(error => (string)error!["code"]!));
        Assert.Equal("field_not_media", Code(notMedia));
        Assert.Equal(ContentErrorCodes.ReferenceNotFound, Code(missingAsset));
        Assert.Equal(0, await agent.QueryAsync(context => context.MediaAssets.CountAsync()));
        Assert.Equal(0, await agent.QueryAsync(context => context.ChangeSets.CountAsync(row => row.SessionId != TestChangeSetTargets.ContentModelSession)));
    }

    // A published product page with a headline and an empty hero image, defined and written by a person.
    private static async Task<Guid> PageAsync(ConnectedAgent agent)
    {
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(new NewContentType(
            "Product page",
            "product-page",
            new ContentTypeDefinition([new FieldDefinition("headline", "text"), new FieldDefinition("hero", "media")])),
            TestChangeSetTargets.ContentModel));
        var page = await agent.AsPersonAsync<IEntryDirectory, Entry>(entries =>
            entries.CreateAsync(new NewEntry("product-page", JsonSerializer.Deserialize<JsonElement>("""{"headline": "The Widget"}"""))));
        await agent.QueryAsync(context => context.Entries
            .Where(row => row.Id == page.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedVersion, 1)));
        return page.Id;
    }

    private static readonly CheckDefinition AltTextCheck = new(
        "altText",
        "Every image a product page shows has alt text.",
        JsonSerializer.Deserialize<JsonElement>("{}"),
        [
            new CheckExample("described", CheckVerdict.Pass, JsonSerializer.Deserialize<JsonElement>("""{"headline": "A drill"}"""), contentType: "product-page"),
            new CheckExample("bare", CheckVerdict.Fail, JsonSerializer.Deserialize<JsonElement>("""{"headline": "![](/drill.jpg)"}"""), contentType: "product-page"),
        ],
        scope: new CheckScope(["product-page"]));

    private static async Task ActiveAltTextCheckAsync(ConnectedAgent agent)
    {
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.CreateAsync(new NewCheck("alt-text", AltTextCheck)));
        await agent.AsPersonAsync<ICheckDirectory, Check>(directory => directory.ActivateAsync("alt-text", 1));
    }

    private static async Task PublishAsPersonAsync(ConnectedAgent agent, string setId)
    {
        var id = Guid.Parse(setId);
        await agent.CallAsync("submit_change_set", new() { ["id"] = setId });
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(id, ChangeSetState.Approved));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.PublishAsync(id));
    }

    private static string? Code(JsonObject failure)
    {
        return (string?)failure["errors"]![0]!["code"];
    }
}
