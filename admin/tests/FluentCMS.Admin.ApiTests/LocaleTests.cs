using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// Locale-aware editing (A-17) on the Blog sample: the status of every locale, a locale added with If-Match "0" as a copy of
// another, the status of a page in another locale read by ids, and a translator whose grants to change are limited to fr.
public sealed class LocaleTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Theory]
    [InlineData(BuiltInRoles.SuperAdminName)]
    [InlineData(BuiltInRoles.EditorName)]
    public async Task ALocaleIsAddedWithIfMatchZeroAsACopyOfAnotherAndEachLocaleHasItsStatus(string role)
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(role);
        var english = await client.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Written in English" }, client.Client.StartAction("en"), Token);
        var id = english.Entry!.Id;
        var before = await client.Client.GetEntryLocalesAsync("article", id, null, Token);

        var french = await client.Client.GetEntryAsync("article", id, "fr", Token);
        var source = await client.Client.GetEntryAsync("article", id, "en", Token);
        var copy = source.Entry!.Fields.DeepClone().AsObject();
        copy["title"] = "Écrit en français";
        var added = await client.Client.SaveEntryAsync("article", id, copy, french, Token);
        var published = await client.Client.PublishEntryAsync("article", id, added, Token);
        var after = await client.Client.GetEntryLocalesAsync("article", id, null, Token);
        var statuses = await client.Client.ListEntriesAsync("article", ListView.StatusQuery("fr", [id]), Token);

        before.Keys.ShouldBe(["en"]);
        (french.IsNewLocale, french.EntityTag, french.Locale).ShouldBe((true, ApiRequest.NewLocale, "fr"));
        (added.Locale, added.Entry!.Locale, added.Entry.Status, added.Entry.Version).ShouldBe(("fr", "fr", "draft", 1));
        published.Read.Entry!.Status.ShouldBe("published");
        after.Keys.Order(StringComparer.Ordinal).ShouldBe(["en", "fr"]);
        (after["en"].Status, after["fr"].Status).ShouldBe(("draft", "published"));
        after["en"].Fields["title"]!.GetValue<string>().ShouldBe("Written in English");
        after["fr"].Fields["title"]!.GetValue<string>().ShouldBe("Écrit en français");
        var row = statuses.Items.ShouldHaveSingleItem();
        (row.Id, row.Status).ShouldBe((id, "published"));
    }

    // Each id of a status read is a node of the filter, beside the filter's own, and the server allows 100.
    [Fact]
    public async Task AStatusReadTakesTheIdsOfABatchAndNotOneMore()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var batch = Enumerable.Range(0, ListView.MaxStatusIds).Select(_ => Guid.CreateVersion7()).ToList();

        var read = await editor.Client.ListEntriesAsync("article", ListView.StatusQuery("fr", batch), Token);
        var refused = await Should.ThrowAsync<ApiProblemException>(() => editor.Client.ListEntriesAsync("article", ListView.StatusQuery("fr", [.. batch, Guid.CreateVersion7()]), Token));

        read.Items.ShouldBeEmpty();
        refused.Problem.Status.ShouldBe(400);
    }

    // A second add of the same locale on "0" meets the version the first one wrote.
    [Fact]
    public async Task AddingALocaleThatAnotherTabAddedFirstIsAConflict()
    {
        await using var admin = new AdminHost();
        using var editor = await admin.SignInAsync(BuiltInRoles.EditorName);
        var english = await editor.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "English" }, editor.Client.StartAction("en"), Token);
        var french = await editor.Client.GetEntryAsync("article", english.Entry!.Id, "fr", Token);
        await editor.Client.SaveEntryAsync("article", english.Entry.Id, new JsonObject { ["title"] = "Premier" }, french, Token);

        var second = await Should.ThrowAsync<ApiProblemException>(() => editor.Client.SaveEntryAsync("article", english.Entry.Id, new JsonObject { ["title"] = "Second" }, french, Token));

        second.Problem.Status.ShouldBe(412);
    }

    // The server narrows /me/access and /schema by the request's locale, so the admin reads them again in the content locale.
    [Fact]
    public async Task ATranslatorLimitedToALocaleMayChangeItAndReadTheOthers()
    {
        await using var admin = new AdminHost();
        using var owner = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        await CreateRoleAsync(owner, "Translator", ("schema.read", null), ("content.*.read", null), ("content.article.update", "fr"), ("content.article.publish", "fr"));
        await CreateRoleAsync(owner, "French reader", ("schema.read", null), ("content.article.read", "fr"));
        var english = await owner.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "English" }, owner.Client.StartAction("en"), Token);
        var id = english.Entry!.Id;
        using var translator = await admin.SignInAsync("Translator");
        using var reader = await admin.SignInAsync("French reader");

        var inEnglish = (await translator.Client.GetMyAccessAsync(null, "en", Token)).Types["article"];
        var inFrench = (await translator.Client.GetMyAccessAsync(null, "fr", Token)).Types["article"];
        var french = await translator.Client.GetEntryAsync("article", id, "fr", Token);
        var added = await translator.Client.SaveEntryAsync("article", id, new JsonObject { ["title"] = "Traduit" }, french, Token);
        var refused = await Should.ThrowAsync<ApiProblemException>(() =>
            translator.Client.SaveEntryAsync("article", id, new JsonObject { ["title"] = "Changed" }, english, Token));

        inEnglish.Update.ShouldBeFalse();
        (inFrench.Update, inFrench.Publish).ShouldBe((true, true));
        inFrench.UpdatableFields.ShouldContain("title");
        (await translator.Client.GetSchemaAsync(null, "en", Token)).Types.ShouldContain(type => type.Name == "article");
        added.Entry!.Fields["title"]!.GetValue<string>().ShouldBe("Traduit");
        refused.Problem.Status.ShouldBeOneOf(403, 404);
        (await translator.Client.GetEntryAsync("article", id, "en", Token)).Entry!.Fields["title"]!.GetValue<string>().ShouldBe("English");

        // A grant to read limited to fr shows the type in fr only.
        (await reader.Client.GetMyAccessAsync(null, "en", Token)).Types.ShouldNotContainKey("article");
        (await reader.Client.GetMyAccessAsync(null, "fr", Token)).Types.ShouldContainKey("article");
        (await reader.Client.GetSchemaAsync(null, "en", Token)).Types.ShouldNotContain(type => type.Name == "article");
        (await reader.Client.GetSchemaAsync(null, "fr", Token)).Types.ShouldContain(type => type.Name == "article");
    }

    private static async Task CreateRoleAsync(AdminClient owner, string name, params (string Permission, string? Locale)[] grants)
    {
        var body = new JsonObject
        {
            ["name"] = name,
            ["grants"] = new JsonArray([.. grants.Select(grant => (JsonNode)new JsonObject
            {
                ["permission"] = grant.Permission,
                ["constraints"] = grant.Locale is null ? null : new JsonObject { ["locales"] = new JsonArray(grant.Locale) },
            })]),
        };
        await owner.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = body, NoSite = true, NoLocale = true }, Token);
    }
}
