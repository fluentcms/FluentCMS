using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The default of the slot, which builds the client of the provider a site names. The providers are local stand-ins that
// answer in the wire format of each, so the provider packages run for real and no provider is called.
public sealed class AiProviderTests : IAsyncLifetime
{
    private const string Marker = "ships-that-must-not-be-logged";

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
            options.AllowedEndpoints.Add(_first.Url());
            options.AllowedEndpoints.Add(_second.Url());
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
        var ollama = await _ai.SiteAsync(settings => Use(settings, AiProvider.Ollama, "model-of-the-second", _second.Url()));
        using var first = await _ai.AdminAsync(compatible);
        using var second = await _ai.AdminAsync(ollama);
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
        two["usage"]!["provider"]!.GetValue<string>().ShouldBe("ollama");
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
        other.Path.ShouldBe("/api/chat");
        other.Body!["model"]!.GetValue<string>().ShouldBe("model-of-the-second");
        other.Body["tools"].ShouldBeNull();

        (await _ai.UsageAsync(compatible)).ShouldHaveSingleItem().Provider.ShouldBe("openAiCompatible");
        (await _ai.UsageAsync(ollama)).ShouldHaveSingleItem().Model.ShouldBe("model-of-the-second");
    }

    [Fact]
    public async Task AnthropicIsReachedThroughItsOwnPackage()
    {
        var site = await _ai.SiteAsync(settings => Use(settings, AiProvider.Anthropic, "model-of-anthropic", _first.Url(), "key-of-the-anthropic-site"));
        using var admin = await _ai.AdminAsync(site);

        var answer = await SuggestAsync(admin, "summary", new { type = "article", field = "summary", text = "Ships are big boats." });

        answer["suggestion"]!.GetValue<string>().ShouldBe("From the stub.");
        answer["usage"]!["provider"]!.GetValue<string>().ShouldBe("anthropic");
        answer["usage"]!["promptTokens"]!.GetValue<long>().ShouldBe(StubProvider.PromptTokens);
        answer["usage"]!["completionTokens"]!.GetValue<long>().ShouldBe(StubProvider.CompletionTokens);
        var sent = _first.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe("/v1/messages");
        sent.Headers["Authorization"].ShouldBe("Bearer key-of-the-anthropic-site");
        sent.Body!["model"]!.GetValue<string>().ShouldBe("model-of-anthropic");
        sent.Body["tools"].ShouldBeNull();
        sent.Body["system"]!.ToJsonString().ShouldContain("Summarise the text");
        sent.Body["messages"]!.AsArray().ShouldHaveSingleItem()!["role"]!.GetValue<string>().ShouldBe("user");
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
        var metadata = await _ai.SiteAsync(settings => Use(settings, AiProvider.Ollama, "a-model", "http://169.254.169.254/"));

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
