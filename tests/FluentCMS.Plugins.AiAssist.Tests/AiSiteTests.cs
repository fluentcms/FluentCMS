using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// What a site decides for itself: whether the plugin is on, its settings, and what the host put in the slot.
public sealed class AiSiteTests : IClassFixture<AiHost>
{
    private const string Settings = "/api/management/v1/settings/ai";

    private static readonly string[] TitleOnly = ["title"];

    private readonly AiHost _ai;

    public AiSiteTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task TheEndpointsAnswerNotFoundForASiteWhereThePluginIsDisabled()
    {
        var off = await _ai.SiteAsync();
        var on = await _ai.SiteAsync();
        using var there = await _ai.AdminAsync(off);
        using var here = await _ai.AdminAsync(on);
        using var anonymous = _ai.Host.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Cms-Site", off.ToString());
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };
        await _ai.SetEnabledAsync("ai", off, false);

        using var action = await ActAsync(there, "rewrite", body);
        using var usage = await there.GetAsync(new Uri(Usage, UriKind.Relative), Token);
        using var notSignedIn = await ActAsync(anonymous, "rewrite", body);
        using var elsewhere = await ActAsync(here, "rewrite", body);

        (await RefusedAsync(action, HttpStatusCode.NotFound)).ShouldBe("FCMS1550");
        (await RefusedAsync(usage, HttpStatusCode.NotFound)).ShouldBe("FCMS1550");
        notSignedIn.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.OK);
        _ai.Chat.Calls.ShouldHaveSingleItem();

        await _ai.SetEnabledAsync("ai", off, true);
        using var back = await ActAsync(there, "rewrite", body);
        back.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AHostsOwnFactoryReplacesTheDefault()
    {
        var site = await _ai.SiteAsync(settings => settings.Model = "model-of-the-host");
        using var admin = await _ai.AdminAsync(site);
        using var instance = await _ai.AdminAsync(DefaultSite.Id);

        var answer = await SuggestAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });
        using var report = await instance.GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        var slot = (await JsonAsync(report))["providerSlots"]!.AsArray().Single(slot => slot!["slot"]!.GetValue<string>() == typeof(IAiClientFactory).FullName)!;

        // The site names OpenAI, for which the default would need a key: the host's factory answered instead.
        answer["usage"]!["provider"]!.GetValue<string>().ShouldBe(ScriptedFactory.Provider);
        answer["usage"]!["model"]!.GetValue<string>().ShouldBe("model-of-the-host");
        slot["implementation"]!.GetValue<string>().ShouldBe(typeof(ScriptedFactory).FullName);
        slot["source"]!.GetValue<string>().ShouldBe("host");
    }

    // Its name ends in tokens, but a number is never a secret by its name.
    [Fact]
    public async Task TheMostTokensOfAnAnswerIsReturnedLikeAnyOtherSetting()
    {
        var site = await _ai.SiteAsync(settings => settings.MaxOutputTokens = 321);
        using var admin = await _ai.AdminAsync(site);

        var read = await ReadAsync(admin);

        read["value"]!["maxOutputTokens"]!.GetValue<int>().ShouldBe(321);
        read["secretsSet"]!.AsArray().Select(key => key!.GetValue<string>()).ShouldNotContain("maxOutputTokens");
        var schema = read["schema"]!["properties"]!["maxOutputTokens"]!.AsObject();
        schema.ContainsKey("writeOnly").ShouldBeFalse();
        schema["default"]!.GetValue<int>().ShouldBe(1024);
    }

    [Fact]
    public async Task TheKeyOfASiteIsWrittenAndNeverReturned()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var read = await ReadAsync(admin);

        using var write = new HttpRequestMessage(HttpMethod.Put, new Uri(Settings, UriKind.Relative))
        {
            Content = JsonContent.Create(new { provider = "anthropic", model = "a-model", apiKey = "key-that-is-never-returned", fields = new { article = TitleOnly } }),
        };
        write.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{read["version"]!.GetValue<long>()}\""));
        using var written = await admin.SendAsync(write, Token);
        var answered = await written.Content.ReadAsStringAsync(Token);
        var after = await ReadAsync(admin);

        written.StatusCode.ShouldBe(HttpStatusCode.OK, answered);
        answered.ShouldNotContain("key-that-is-never-returned");
        after.ToJsonString().ShouldNotContain("key-that-is-never-returned");
        after["value"]!["provider"]!.GetValue<string>().ShouldBe("anthropic");
        after["value"]!["apiKey"].ShouldBeNull();
        after["secretsSet"]!.AsArray().Select(key => key!.GetValue<string>()).ShouldContain("apiKey");
        after["schema"]!["properties"]!["apiKey"]!["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains("key-that-is-never-returned", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "endpoint": "ftp://models.example.com" }""")]
    [InlineData("""{ "endpoint": "https://user:secret@models.example.com/v1" }""")]
    [InlineData("""{ "endpoint": "https://models.example.com/v1?key=1" }""")]
    [InlineData("""{ "endpoint": "models.example.com" }""")]
    [InlineData("""{ "fields": { "article": ["a field"] } }""")]
    [InlineData("""{ "fields": { "article": null } }""")]
    [InlineData("""{ "fields": null }""")]
    [InlineData("""{ "maxOutputTokens": 0 }""")]
    [InlineData("""{ "requestTimeout": 0 }""")]
    [InlineData("""{ "callsPerMinute": 0 }""")]
    public async Task SettingsThatCannotBeUsedAreRefused(string document)
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var read = await ReadAsync(admin);

        using var write = new HttpRequestMessage(HttpMethod.Put, new Uri(Settings, UriKind.Relative)) { Content = new StringContent(document, System.Text.Encoding.UTF8, "application/json") };
        write.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{read["version"]!.GetValue<long>()}\""));
        using var written = await admin.SendAsync(write, Token);

        (await RefusedAsync(written, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS2805");
    }

    private static async Task<JsonNode> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri(Settings, UriKind.Relative), Token);
        return await JsonAsync(response);
    }
}
