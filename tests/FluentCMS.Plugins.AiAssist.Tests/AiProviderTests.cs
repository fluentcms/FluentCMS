using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The default of the slot, which builds the client of the provider a site names. The providers are local stand-ins that
// answer in the chat completions format of OpenAI, so the package runs for real and no provider is called.
public sealed class AiProviderTests : IAsyncLifetime
{
    private const string Marker = "ships-that-must-not-be-logged";

    private static readonly string[] SummaryOnly = ["summary"];

    private StubProvider _first = null!;
    private StubProvider _second = null!;
    private StubProvider _unlisted = null!;
    private AiHost _ai = null!;

    public async ValueTask InitializeAsync()
    {
        _first = await StubProvider.StartAsync();
        _second = await StubProvider.StartAsync();
        _unlisted = await StubProvider.StartAsync();
        _ai = new AiHost(scripted: false, options =>
        {
            options.AllowedEndpoints.Add(_first.Url("v1"));
            options.AllowedEndpoints.Add(_second.Url("v1"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _ai.DisposeAsync();
        await _first.DisposeAsync();
        await _second.DisposeAsync();
        await _unlisted.DisposeAsync();
    }

    [Fact]
    public async Task TwoSitesWithDifferentProvidersAndModelsEachUseTheirOwn()
    {
        var compatible = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "model-of-the-first", _first.Url("v1"), "key-of-the-first-site"));
        var azure = await _ai.SiteAsync(settings => Use(settings, AiProvider.AzureOpenAi, "model-of-the-second", _second.Url("v1"), "key-of-the-second-site"));
        using var first = await _ai.AdminAsync(compatible);
        using var second = await _ai.AdminAsync(azure);
        _first.Answer = "From the first.";
        _second.Answer = "From the second.";

        var one = await SuggestAsync(first, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });
        var two = await SuggestAsync(second, "rewrite", new { type = "article", field = "summary", text = "Boats are small ships." });

        one["suggestion"]!.GetValue<string>().ShouldBe("From the first.");
        one["usage"]!["provider"]!.GetValue<string>().ShouldBe("openAiCompatible");
        one["usage"]!["model"]!.GetValue<string>().ShouldBe("model-of-the-first");
        one["usage"]!["promptTokens"]!.GetValue<long>().ShouldBe(StubProvider.PromptTokens);
        one["usage"]!["completionTokens"]!.GetValue<long>().ShouldBe(StubProvider.CompletionTokens);
        two["suggestion"]!.GetValue<string>().ShouldBe("From the second.");
        two["usage"]!["provider"]!.GetValue<string>().ShouldBe("azureOpenAi");
        two["usage"]!["model"]!.GetValue<string>().ShouldBe("model-of-the-second");
        two["usage"]!["promptTokens"]!.GetValue<long>().ShouldBe(StubProvider.PromptTokens);

        // On the wire: the site's key, its model, the instructions as the system message, the text as the user message,
        // and no tools.
        var sent = _first.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe("/v1/chat/completions");
        sent.Headers["Authorization"].ShouldBe("Bearer key-of-the-first-site");
        sent.Body!["model"]!.GetValue<string>().ShouldBe("model-of-the-first");
        sent.Body["tools"].ShouldBeNull();
        var messages = sent.Body["messages"]!.AsArray();
        messages.Select(message => message!["role"]!.GetValue<string>()).ShouldBe(["system", "user"]);
        ContentOf(messages[0]!).ShouldStartWith("Rewrite the text");
        ContentOf(messages[1]!).ShouldBe("Ships are big boats.");

        var other = _second.Requests.ShouldHaveSingleItem();
        other.Path.ShouldBe("/v1/chat/completions");
        other.Headers["Authorization"].ShouldBe("Bearer key-of-the-second-site");
        other.Body!["model"]!.GetValue<string>().ShouldBe("model-of-the-second");
        other.Body["tools"].ShouldBeNull();

        (await _ai.UsageAsync(compatible)).ShouldHaveSingleItem().Provider.ShouldBe("openAiCompatible");
        (await _ai.UsageAsync(azure)).ShouldHaveSingleItem().Model.ShouldBe("model-of-the-second");
    }

    // Ollama is reached at its /v1 endpoint, which takes no key.
    [Fact]
    public async Task AnOpenAiCompatibleEndpointWithoutAKeyIsCalledStreamedAndNot()
    {
        var site = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "llama3.2", _second.Url("v1")));
        using var admin = await _ai.AdminAsync(site);
        _second.Answer = "Ships are large vessels.";
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };

        var plain = await SuggestAsync(admin, "rewrite", body);
        using var response = await ActAsync(admin, "rewrite", body, stream: true);
        var events = StreamEvents(await response.Content.ReadAsStringAsync(Token));

        plain["suggestion"]!.GetValue<string>().ShouldBe("Ships are large vessels.");
        plain["usage"]!["provider"]!.GetValue<string>().ShouldBe("openAiCompatible");
        plain["usage"]!["model"]!.GetValue<string>().ShouldBe("llama3.2");
        events[^1].Event.ShouldBe("done");
        events[^1].Data["suggestion"]!.GetValue<string>().ShouldBe("Ships are large vessels.");
        events[^1].Data["usage"]!["completionTokens"]!.GetValue<long>().ShouldBe(StubProvider.CompletionTokens);
        _second.Requests.Count.ShouldBe(2);
        _second.Requests.ShouldAllBe(request => request.Path == "/v1/chat/completions" && !request.Headers.ContainsKey("Authorization"));
        _second.Requests.Select(request => request.Body!["stream"]?.GetValue<bool>() == true).ShouldBe([false, true]);
        var usage = await _ai.UsageAsync(site);
        usage.Count.ShouldBe(2);
        usage.ShouldAllBe(row => row.Succeeded && row.Provider == "openAiCompatible" && row.Model == "llama3.2" && row.PromptTokens == StubProvider.PromptTokens && row.CompletionTokens == StubProvider.CompletionTokens);
    }

    // A site whose settings were saved while the plugin had Anthropic and Ollama keeps them, and is told what to use.
    [Theory]
    [InlineData("anthropic")]
    [InlineData("ollama")]
    public async Task ASiteThatNamesARemovedProviderIsRefusedWithWhatToUse(string provider)
    {
        var site = await _ai.BareSiteAsync();
        await _ai.StoreSettingsAsync(site, $$$"""{"provider":"{{{provider}}}","model":"a-model","endpoint":"{{{_first.Url("v1")}}}","apiKey":"key-of-the-site","fields":{"article":["summary"]}}""");
        using var admin = await _ai.AdminAsync(site);

        using var settings = await admin.GetAsync(new Uri("/api/management/v1/settings/ai", UriKind.Relative), Token);
        using var refused = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });
        var problem = await JsonAsync(refused, HttpStatusCode.Conflict);

        (await JsonAsync(settings))["value"]!["provider"]!.GetValue<string>().ShouldBe(provider);
        problem["code"]!.GetValue<string>().ShouldBe("FCMS1692");
        problem["detail"]!.GetValue<string>().ShouldContain("openAiCompatible");
        problem["detail"]!.GetValue<string>().ShouldContain(nameof(IAiClientFactory));
        _ai.Logs.Of(9368).ShouldHaveSingleItem().ShouldContain(provider);
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains("key-of-the-site", StringComparison.Ordinal));
        _first.Requests.ShouldBeEmpty();
        (await _ai.UsageAsync(site)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AStreamedAnswerOfAProviderCarriesItsUsage()
    {
        var site = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "model-of-the-first", _first.Url("v1"), "key-of-the-first-site"));
        using var admin = await _ai.AdminAsync(site);
        _first.Answer = "Ships are large vessels.";

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." }, stream: true);
        var events = StreamEvents(await response.Content.ReadAsStringAsync(Token));

        events[^1].Event.ShouldBe("done");
        string.Concat(events.Where(sent => sent.Event == "delta").Select(sent => sent.Data["text"]!.GetValue<string>())).ShouldBe("Ships are large vessels.");
        events[^1].Data["suggestion"]!.GetValue<string>().ShouldBe("Ships are large vessels.");
        events[^1].Data["usage"]!["promptTokens"]!.GetValue<long>().ShouldBe(StubProvider.PromptTokens);
        events[^1].Data["usage"]!["completionTokens"]!.GetValue<long>().ShouldBe(StubProvider.CompletionTokens);
    }

    [Fact]
    public async Task ASiteWithNoProviderOrWithOneThatIsNotConfiguredIsRefused()
    {
        var none = await _ai.SiteAsync(settings => settings.Provider = AiProvider.None);
        var noKey = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAi, "a-model"));
        var noEndpoint = await _ai.SiteAsync(settings => Use(settings, AiProvider.AzureOpenAi, "a-deployment", key: "key-of-the-azure-site"));
        var noModel = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, null, _first.Url("v1"), "key-of-the-first-site"));

        (await RefusalAsync(none)).ShouldBe((HttpStatusCode.Conflict, "FCMS1682"));
        (await RefusalAsync(noKey)).ShouldBe((HttpStatusCode.Conflict, "FCMS1685"));
        (await RefusalAsync(noEndpoint)).ShouldBe((HttpStatusCode.Conflict, "FCMS1685"));
        (await RefusalAsync(noModel)).ShouldBe((HttpStatusCode.Conflict, "FCMS1685"));
        _first.Requests.ShouldBeEmpty();
        (await _ai.UsageAsync(noKey)).ShouldBeEmpty();
    }

    // The allow-list is what keeps a site administrator from pointing the server at an address inside the network.
    [Fact]
    public async Task AnEndpointOutsideTheAllowListIsRefusedAndNeverCalled()
    {
        var unlisted = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "a-model", _unlisted.Url("v1"), "key-of-the-site"));
        var otherPath = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "a-model", _first.Url("internal/v1"), "key-of-the-site"));
        var ownEndpoint = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAi, "a-model", _unlisted.Url("v1"), "key-of-the-site"));
        var metadata = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "a-model", "http://169.254.169.254/"));

        (await RefusalAsync(unlisted)).ShouldBe((HttpStatusCode.Conflict, "FCMS1684"));
        (await RefusalAsync(otherPath)).ShouldBe((HttpStatusCode.Conflict, "FCMS1684"));
        (await RefusalAsync(ownEndpoint)).ShouldBe((HttpStatusCode.Conflict, "FCMS1684"));
        (await RefusalAsync(metadata)).ShouldBe((HttpStatusCode.Conflict, "FCMS1684"));

        _unlisted.Requests.ShouldBeEmpty();
        _first.Requests.ShouldBeEmpty();
        (await _ai.UsageAsync(unlisted)).ShouldBeEmpty();

        // The log names the host that was refused, and nothing of the key.
        var refusals = _ai.Logs.Of(9366);
        refusals.Count.ShouldBe(4);
        refusals.ShouldContain(line => line.Contains("169.254.169.254", StringComparison.Ordinal));
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains("key-of-the-site", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAllowedEndpointThatRedirectsIsNotFollowed()
    {
        var site = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "a-model", _first.Url("v1"), "key-of-the-site"));
        _first.RedirectTo = new Uri(_unlisted.Root, "v1/chat/completions");

        (await RefusalAsync(site)).ShouldBe((HttpStatusCode.BadGateway, "FCMS1689"));

        _first.Requests.ShouldHaveSingleItem();
        _unlisted.Requests.ShouldBeEmpty();
        (await _ai.UsageAsync(site)).ShouldHaveSingleItem().Succeeded.ShouldBeFalse();
    }

    // A provider's failure may repeat what it was sent, so its message reaches neither the caller nor the log.
    [Fact]
    public async Task AProviderThatRefusesIsAFailedCallWhoseMessageIsKeptOutOfTheAnswerAndTheLog()
    {
        var site = await _ai.SiteAsync(settings => Use(settings, AiProvider.OpenAiCompatible, "a-model", _first.Url("v1"), "key-of-the-site"));
        using var admin = await _ai.AdminAsync(site);
        _first.Status = 401;

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = $"Ships are big boats, {Marker}." });
        var problem = await JsonAsync(response, HttpStatusCode.BadGateway);

        problem["code"]!.GetValue<string>().ShouldBe("FCMS1689");
        problem.ToJsonString().ShouldNotContain(Marker);
        _first.Requests.ShouldHaveSingleItem();
        _ai.Logs.Of(9361).ShouldHaveSingleItem().ShouldContain("status 401");
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains(Marker, StringComparison.Ordinal));
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains("key-of-the-site", StringComparison.Ordinal));
        (await _ai.UsageAsync(site)).ShouldHaveSingleItem().Succeeded.ShouldBeFalse();
    }

    // A site's first save without a key takes the key of the instance defaults with it, since a form never shows a secret
    // and must not wipe it (spec 4.13). A site administrator who then names another endpoint does not get that key sent
    // there: it stays with the provider and the endpoint of the defaults.
    [Fact]
    public async Task AKeyASiteTookFromTheInstanceDefaultsIsNeverSentToAnEndpointTheSiteNames()
    {
        await _ai.SetDefaultsAsync(new AiSettings
        {
            Provider = AiProvider.OpenAiCompatible,
            Model = "model-of-the-instance",
            Endpoint = _first.Url("v1"),
            ApiKey = "key-of-the-instance",
            Fields = { ["article"] = ["summary"] },
        });
        using var inheriting = await _ai.AdminAsync(await _ai.BareSiteAsync());
        using var sameEndpoint = await _ai.AdminAsync(await _ai.BareSiteAsync());
        var movedSite = await _ai.BareSiteAsync();
        using var moved = await _ai.AdminAsync(movedSite);
        using var owning = await _ai.AdminAsync(await _ai.BareSiteAsync());
        using var keyless = await _ai.AdminAsync(await _ai.BareSiteAsync());
        var fields = new { article = SummaryOnly };
        var body = new { type = "article", field = "summary", text = "Ships are big boats." };

        // None of the first three sends a key; the fourth brings its own, and the last clears the key, as for Ollama.
        await SaveAsync(sameEndpoint, new { provider = "openAiCompatible", model = "model-of-the-site", endpoint = _first.Url("v1"), fields });
        var saved = await SaveAsync(moved, new { provider = "openAiCompatible", model = "model-of-the-site", endpoint = _second.Url("v1"), fields });
        await SaveAsync(owning, new { provider = "openAiCompatible", model = "model-of-the-site", endpoint = _second.Url("v1"), apiKey = "key-of-the-site", fields });
        await SaveAsync(keyless, new { provider = "openAiCompatible", model = "model-without-a-key", endpoint = _second.Url("v1"), apiKey = (string?)null, fields });

        var followed = await SuggestAsync(inheriting, "rewrite", body);
        var kept = await SuggestAsync(sameEndpoint, "rewrite", body);
        using var refused = await ActAsync(moved, "rewrite", body);
        var problem = await JsonAsync(refused, HttpStatusCode.Conflict);
        var own = await SuggestAsync(owning, "rewrite", body);
        var withoutKey = await SuggestAsync(keyless, "rewrite", body);

        // The site did take the key with it, which is what makes the refusal necessary.
        saved["secretsSet"]!.AsArray().Select(key => key!.GetValue<string>()).ShouldContain("apiKey");
        problem["code"]!.GetValue<string>().ShouldBe("FCMS1685");
        problem.ToJsonString().ShouldNotContain("key-of-the-instance");
        followed["usage"]!["model"]!.GetValue<string>().ShouldBe("model-of-the-instance");
        kept["usage"]!["model"]!.GetValue<string>().ShouldBe("model-of-the-site");
        own["usage"]!["model"]!.GetValue<string>().ShouldBe("model-of-the-site");
        withoutKey["usage"]!["model"]!.GetValue<string>().ShouldBe("model-without-a-key");
        _first.Requests.Select(request => request.Headers["Authorization"]).ShouldBe(["Bearer key-of-the-instance", "Bearer key-of-the-instance"]);
        _second.Requests.Select(request => request.Headers.GetValueOrDefault("Authorization")).ShouldBe(["Bearer key-of-the-site", null]);
        (await _ai.UsageAsync(movedSite)).ShouldBeEmpty();
        _ai.Logs.Lines.ShouldNotContain(line => line.Contains("key-of-the-instance", StringComparison.Ordinal));
    }

    // The slot keeps the plugin's default until a host fills it.
    [Fact]
    public async Task TheStartupReportListsTheDefaultOfTheSlotWithThePlugin()
    {
        using var admin = await _ai.AdminAsync(FluentCMS.Core.Sites.DefaultSite.Id);

        using var response = await admin.GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        var slot = (await JsonAsync(response))["providerSlots"]!.AsArray().Single(slot => slot!["slot"]!.GetValue<string>() == typeof(IAiClientFactory).FullName)!;

        slot["implementation"]!.GetValue<string>().ShouldBe(typeof(AiClientFactory).FullName);
        slot["source"]!.GetValue<string>().ShouldBe("default");
        slot["plugin"]!.GetValue<string>().ShouldBe("ai");
    }

    // The settings of the site of the client, saved through the Management API as its administrator saves them, and read
    // back as the API shows them.
    private static async Task<JsonNode> SaveAsync(HttpClient admin, object document)
    {
        var path = new Uri("/api/management/v1/settings/ai", UriKind.Relative);
        using var read = await admin.GetAsync(path, Token);
        using var write = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(document) };
        write.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{(await JsonAsync(read))["version"]!.GetValue<long>()}\""));
        using var written = await admin.SendAsync(write, Token);
        await JsonAsync(written);
        using var after = await admin.GetAsync(path, Token);
        return await JsonAsync(after);
    }

    private static void Use(AiSettings settings, AiProvider provider, string? model, string? endpoint = null, string? key = null) =>
        (settings.Provider, settings.Model, settings.Endpoint, settings.ApiKey) = (provider, model, endpoint, key);

    private async Task<(HttpStatusCode Status, string Code)> RefusalAsync(Guid site)
    {
        using var admin = await _ai.AdminAsync(site);
        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });
        return (response.StatusCode, (await JsonAsync(response, response.StatusCode))["code"]!.GetValue<string>());
    }

    // The content of a chat completions message, which is a text or a list of parts.
    private static string ContentOf(JsonNode message) =>
        message["content"] is JsonArray parts ? string.Concat(parts.Select(part => part!["text"]!.GetValue<string>())) : message["content"]!.GetValue<string>();
}
