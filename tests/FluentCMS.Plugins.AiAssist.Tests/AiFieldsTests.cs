using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// GET /ai/fields, which tells an editor where the actions work, by the checks of POST /ai/actions/{action}.
public sealed class AiFieldsTests : IClassFixture<AiHost>
{
    private readonly AiHost _ai;

    public AiFieldsTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task AnEditorGetsTheOptedInFieldsItMayUpdateAndNotThoseItMayOnlyRead()
    {
        // rating takes no actions and missing is no field of the type, so neither is listed.
        var site = await _ai.SiteAsync(settings => settings.Fields["article"] = ["body", "summary", "title", "rating", "missing"]);
        using var admin = await _ai.AdminAsync(site);
        using var editor = await _ai.CallerAsync(
            site,
            (AiPermissions.Use, null),
            ("content.article.read", null),
            ("content.article.update", new PermissionConstraints { Fields = ["title", "notes", "rating"] }));

        var all = await FieldsAsync(admin);
        var some = await FieldsAsync(editor);

        all["enabled"]!.GetValue<bool>().ShouldBeTrue();
        all["locale"]!.GetValue<string>().ShouldBe("en");
        all["fields"]!.ToJsonString().ShouldBe("""{"article":["title","summary","body"]}""");
        some["fields"]!.ToJsonString().ShouldBe("""{"article":["title"]}""");

        // The actions agree.
        using var listed = await ActAsync(editor, "title", new { type = "article", field = "title", text = "Ships are big boats." });
        using var readOnly = await ActAsync(editor, "shorten", new { type = "article", field = "summary", text = "Ships are big boats." });
        using var notOptedIn = await ActAsync(editor, "rewrite", new { type = "article", field = "notes", text = "Ships are big boats." });
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefusedAsync(readOnly, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        (await RefusedAsync(notOptedIn, HttpStatusCode.Forbidden)).ShouldBe("FCMS1683");
    }

    // A type the caller may not read is not listed even when it may update it, and a field it may set on a new entry only
    // is not listed either: the actions refuse both.
    [Fact]
    public async Task ATypeTheCallerMayNotReadAndAFieldItMayOnlyCreateAreLeftOut()
    {
        var site = await _ai.SiteAsync();
        using var readers = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("content.article.read", null));
        using var blind = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("content.article.update", null));
        using var creators = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("content.article.read", null), ("content.article.create", null));

        foreach (var caller in (HttpClient[])[readers, blind, creators])
        {
            var answer = await FieldsAsync(caller);
            answer["enabled"]!.GetValue<bool>().ShouldBeTrue();
            answer["fields"]!.ToJsonString().ShouldBe("{}");

            using var refused = await ActAsync(caller, "title", new { type = "article", field = "title", text = "Ships are big boats." });
            (await RefusedAsync(refused, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        }

        _ai.Chat.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheFieldsAreThoseTheCallerMayUpdateInTheLocaleOfTheRequest()
    {
        var site = await _ai.SiteAsync(null, "fr");
        using var editor = await _ai.CallerAsync(
            site,
            (AiPermissions.Use, null),
            ("content.article.read", null),
            ("content.article.update", new PermissionConstraints { Locales = ["fr"] }));

        var english = await FieldsAsync(editor);
        var french = await FieldsAsync(editor, "fr");

        english["locale"]!.GetValue<string>().ShouldBe("en");
        english["fields"]!.ToJsonString().ShouldBe("{}");
        french["locale"]!.GetValue<string>().ShouldBe("fr");
        french["fields"]!.ToJsonString().ShouldBe("""{"article":["title","summary","body"]}""");
    }

    [Fact]
    public async Task ASiteWithoutAProviderHasAiAssistOff()
    {
        var none = await _ai.SiteAsync(settings => settings.Provider = AiProvider.None);
        var bare = await _ai.BareSiteAsync();
        var removed = await _ai.BareSiteAsync();
        await _ai.StoreSettingsAsync(removed, """{"provider":"anthropic","model":"a-model","fields":{"article":["title"]}}""");

        foreach (var site in (Guid[])[none, bare, removed])
        {
            using var admin = await _ai.AdminAsync(site);
            var answer = await FieldsAsync(admin);
            answer["enabled"]!.GetValue<bool>().ShouldBeFalse();
            answer["fields"]!.ToJsonString().ShouldBe("{}");
        }

        using var off = await _ai.AdminAsync(none);
        using var refused = await ActAsync(off, "title", new { type = "article", field = "title", text = "Ships are big boats." });
        (await RefusedAsync(refused, HttpStatusCode.Conflict)).ShouldBe("FCMS1682");
    }

    [Fact]
    public async Task ACallerWithoutAiUseIsForbidden()
    {
        var site = await _ai.SiteAsync();
        using var editor = await _ai.CallerAsync(site, ("content.article.read", null), ("content.article.update", null));
        using var anonymous = _ai.Host.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Cms-Site", site.ToString());

        using var forbidden = await editor.GetAsync(new Uri(Fields, UriKind.Relative), Token);
        using var notSignedIn = await anonymous.GetAsync(new Uri(Fields, UriKind.Relative), Token);

        (await RefusedAsync(forbidden, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        notSignedIn.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheAnswerHoldsNothingOfTheProvidersConfiguration()
    {
        var site = await _ai.SiteAsync(settings =>
        {
            settings.Provider = AiProvider.OpenAiCompatible;
            settings.Model = "model-of-the-site";
            settings.Endpoint = "https://models.example.com/v1";
            settings.ApiKey = "key-that-is-never-returned";
        });
        using var admin = await _ai.AdminAsync(site);

        using var response = await admin.GetAsync(new Uri(Fields, UriKind.Relative), Token);
        var raw = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        JsonNode.Parse(raw)!.AsObject().Select(property => property.Key).ShouldBe(["enabled", "locale", "fields", "assets"]);
        foreach (var held in (string[])["model-of-the-site", "models.example.com", "key-that-is-never-returned", "openAiCompatible", "provider", "model", "endpoint", "apiKey"])
        {
            raw.ShouldNotContain(held, Case.Insensitive);
        }
    }

    [Fact]
    public async Task TheOpenApiDocumentListsTheRouteWithItsBody()
    {
        var provider = _ai.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = JsonNode.Parse(await (await provider.GetOpenApiDocumentAsync(Token)).SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, Token))!;

        var answer = document["paths"]![Fields]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!;
        answer["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/AiFields");
        document["components"]!["schemas"]!["AiFields"]!["properties"]!.AsObject().Select(property => property.Key).ShouldBe(["enabled", "locale", "fields", "assets"]);
        document["components"]!["schemas"]!["AiUsageRow"]!["properties"]!.AsObject().Select(property => property.Key).ShouldBe(["id", "principalKind", "principalId", "principalName", "action", "type", "field", "provider", "model", "promptTokens", "completionTokens", "durationMs", "succeeded", "createdAt"]);
        document["components"]!["schemas"]!["AiUsageTotals"]!["properties"]!.AsObject().Select(property => property.Key).ShouldBe(["calls", "promptTokens", "completionTokens", "groups", "groupsTruncated"]);
        document["components"]!["schemas"]!["AiUsageGroup"]!["properties"]!.AsObject().Select(property => property.Key).ShouldBe(["principalKind", "principalId", "action", "calls", "promptTokens", "completionTokens"]);
    }

    // altText for an asset alone needs the site's assetAltText, the media library and media.read and media.update in some
    // folder.
    [Fact]
    public async Task AssetsSaysWhetherTheCallerMayAskForTheAltTextOfAnAssetAlone()
    {
        var site = await _ai.SiteAsync(settings => settings.AssetAltText = true);
        var noMedia = await _ai.SiteAsync(settings => settings.AssetAltText = true);
        var off = await _ai.SiteAsync(settings => (settings.Provider, settings.AssetAltText) = (AiProvider.None, true));
        var notTurnedOn = await _ai.SiteAsync();
        await _ai.SetEnabledAsync("media", noMedia, false);
        using var admin = await _ai.AdminAsync(site);
        using var notAsked = await _ai.AdminAsync(notTurnedOn);
        using var limited = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("media.read", null), ("media.update", new PermissionConstraints { Resources = [Guid.CreateVersion7().ToString("D")] }));
        using var readers = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("media.read", null));
        using var writers = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("media.update", null));
        using var withoutMedia = await _ai.AdminAsync(noMedia);
        using var withoutProvider = await _ai.AdminAsync(off);

        (await FieldsAsync(admin))["assets"]!.GetValue<bool>().ShouldBeTrue();
        (await FieldsAsync(limited))["assets"]!.GetValue<bool>().ShouldBeTrue();
        (await FieldsAsync(readers))["assets"]!.GetValue<bool>().ShouldBeFalse();
        (await FieldsAsync(writers))["assets"]!.GetValue<bool>().ShouldBeFalse();
        (await FieldsAsync(withoutMedia))["assets"]!.GetValue<bool>().ShouldBeFalse();
        (await FieldsAsync(withoutProvider))["assets"]!.GetValue<bool>().ShouldBeFalse();
        var answer = await FieldsAsync(notAsked);
        answer["assets"]!.GetValue<bool>().ShouldBeFalse();
        (answer["enabled"]!.GetValue<bool>(), answer["fields"]!.ToJsonString()).ShouldBe((true, """{"article":["title","summary","body"]}"""));
    }

    private static async Task<JsonNode> FieldsAsync(HttpClient client, string? locale = null)
    {
        using var response = await client.GetAsync(new Uri(Fields + (locale is null ? "" : $"?locale={locale}"), UriKind.Relative), Token);
        return await JsonAsync(response);
    }
}
