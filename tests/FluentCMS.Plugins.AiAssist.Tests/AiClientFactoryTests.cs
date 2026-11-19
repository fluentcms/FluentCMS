using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The default factory on its own: where the key comes from, and which endpoints a site may name.
public sealed class AiClientFactoryTests
{
    private static readonly SiteId Site = new(Guid.CreateVersion7());

    // As core reads and writes settings.
    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AKeyThatTheSettingsDoNotHoldComesFromTheConfigurationOfItsProvider()
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], new()
        {
            ["FluentCms:Ai:OpenAiCompatible:ApiKey"] = "key-of-the-configuration",
            ["FluentCms:Ai:OpenAiCompatible:Endpoint"] = stub.Url("v1"),
            ["FluentCms:Ai:AzureOpenAi:ApiKey"] = "key-of-another-provider",
            ["FluentCms:Ai:AzureOpenAi:Endpoint"] = stub.Url("v1"),
        });
        List<ChatMessage> messages = [new(ChatRole.User, "Ships are big boats.")];

        var fromConfiguration = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = stub.Url("v1") }, Token);
        await fromConfiguration!.Client.GetResponseAsync(messages, cancellationToken: Token);
        var fromSettings = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = stub.Url("v1"), ApiKey = "key-of-the-site" }, Token);
        await fromSettings!.Client.GetResponseAsync(messages, cancellationToken: Token);

        (fromConfiguration.Provider, fromConfiguration.Model).ShouldBe(("openAiCompatible", "a-model"));
        fromConfiguration.SiteCredential.ShouldBeFalse();
        fromSettings.SiteCredential.ShouldBeTrue();
        stub.Requests.Select(request => request.Headers["Authorization"]).ShouldBe(["Bearer key-of-the-configuration", "Bearer key-of-the-site"]);
    }

    // A local endpoint such as Ollama's /v1 takes no key, so an OpenAI-compatible one is called without any. OpenAI and Azure
    // OpenAI still need one.
    [Fact]
    public async Task OnlyAnOpenAiCompatibleEndpointIsCalledWithoutAKey()
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], []);

        var keyless = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = stub.Url("v1") }, Token);
        await keyless!.Client.GetResponseAsync([new(ChatRole.User, "Ships are big boats.")], cancellationToken: Token);
        var openAi = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model" }, Token));
        var azure = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.AzureOpenAi, Model = "a-deployment", Endpoint = stub.Url("v1") }, Token));

        var sent = stub.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe("/v1/chat/completions");
        sent.Headers.ShouldNotContainKey("Authorization");
        openAi.Code.ShouldBe("FCMS1685");
        azure.Code.ShouldBe("FCMS1685");
    }

    // The operator's key for OpenAI is not a site's to send to another endpoint, even an allowed one.
    [Fact]
    public async Task TheConfigurationsKeyForOpenAiGoesToItsOwnEndpointOnly()
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], new() { ["FluentCms:Ai:OpenAi:ApiKey"] = "key-of-the-operator" });

        var atItsOwn = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model" }, Token);
        var elsewhere = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", Endpoint = stub.Url("v1") }, Token));
        var withItsOwnKey = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", Endpoint = stub.Url("v1"), ApiKey = "key-of-the-site" }, Token);

        atItsOwn.ShouldNotBeNull();
        elsewhere.Code.ShouldBe("FCMS1685");
        withItsOwnKey.ShouldNotBeNull();
        stub.Requests.ShouldBeEmpty();
    }

    // As OpenAI's: the key of Azure OpenAI or of an OpenAI-compatible endpoint in the configuration goes to the endpoint it
    // is configured for, and not to another allowed endpoint a site names. A compatible endpoint is called without it.
    [Fact]
    public async Task TheConfigurationsKeyOfEveryProviderGoesToItsOwnEndpointOnly()
    {
        await using var own = await StubProvider.StartAsync();
        await using var other = await StubProvider.StartAsync();
        using var factory = Factory([own.Url("v1"), other.Url("v1")], new()
        {
            ["FluentCms:Ai:AzureOpenAi:ApiKey"] = "key-of-the-azure-account",
            ["FluentCms:Ai:AzureOpenAi:Endpoint"] = own.Url("v1/"),
            ["FluentCms:Ai:OpenAiCompatible:ApiKey"] = "key-of-the-gateway",
            ["FluentCms:Ai:OpenAiCompatible:Endpoint"] = own.Url("v1"),
        });
        List<ChatMessage> messages = [new(ChatRole.User, "Ships are big boats.")];

        var azure = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.AzureOpenAi, Model = "a-deployment", Endpoint = own.Url("v1") }, Token);
        await azure!.Client.GetResponseAsync(messages, cancellationToken: Token);
        var compatible = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = own.Url("v1") }, Token);
        await compatible!.Client.GetResponseAsync(messages, cancellationToken: Token);
        var azureElsewhere = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.AzureOpenAi, Model = "a-deployment", Endpoint = other.Url("v1") }, Token));
        var compatibleElsewhere = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1") }, Token);
        await compatibleElsewhere!.Client.GetResponseAsync(messages, cancellationToken: Token);

        own.Requests.Select(request => request.Headers["Authorization"]).ShouldBe(["Bearer key-of-the-azure-account", "Bearer key-of-the-gateway"]);
        azureElsewhere.Code.ShouldBe("FCMS1685");
        azureElsewhere.Message.ShouldContain("FluentCms:Ai:AzureOpenAi:Endpoint");
        azureElsewhere.Message.ShouldNotContain("key-of-the-azure-account");
        other.Requests.ShouldHaveSingleItem().Headers.ShouldNotContainKey("Authorization");
    }

    // The key of OpenAI may name another endpoint, such as a proxy of the operator's, and then goes there and not to OpenAI.
    [Fact]
    public async Task TheConfigurationsKeyForOpenAiMayNameItsEndpoint()
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], new() { ["FluentCms:Ai:OpenAi:ApiKey"] = "key-of-the-operator", ["FluentCms:Ai:OpenAi:Endpoint"] = stub.Url("v1") });

        var atItsEndpoint = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", Endpoint = stub.Url("v1") }, Token);
        await atItsEndpoint!.Client.GetResponseAsync([new(ChatRole.User, "Ships are big boats.")], cancellationToken: Token);
        var atOpenAi = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model" }, Token));

        stub.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe("Bearer key-of-the-operator");
        atOpenAi.Code.ShouldBe("FCMS1685");
    }

    // The endpoint of a key is compared as the allow-list compares: the case of the host, the default port written out and
    // a slash at the end do not make another endpoint, and another path or port does.
    [Theory]
    [InlineData("https://Models.Example.COM:443/v1/", true)]
    [InlineData("https://models.example.com/v1", true)]
    [InlineData("https://models.example.com:8443/v1", false)]
    [InlineData("https://models.example.com/v2", false)]
    [InlineData("http://models.example.com/v1", false)]
    public async Task TheEndpointOfAKeyIsComparedAsTheAllowListCompares(string configured, bool sent)
    {
        using var factory = Factory(["https://models.example.com/v1"], new() { ["FluentCms:Ai:AzureOpenAi:ApiKey"] = "key-of-the-azure-account", ["FluentCms:Ai:AzureOpenAi:Endpoint"] = configured });
        var settings = new AiSettings { Provider = AiProvider.AzureOpenAi, Model = "a-deployment", Endpoint = "https://MODELS.example.com/v1" };

        if (sent)
        {
            (await factory.CreateAsync(Site, settings, Token)).ShouldNotBeNull();
        }
        else
        {
            (await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, settings, Token))).Code.ShouldBe("FCMS1685");
        }
    }

    [Fact]
    public async Task TheKeyOfOpenAiGoesToItsOwnEndpointWrittenWithItsDefaultPort()
    {
        using var factory = Factory(["https://API.openai.com:443/v1/"], new() { ["FluentCms:Ai:OpenAi:ApiKey"] = "key-of-the-operator" });

        var written = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", Endpoint = "https://API.openai.com:443/v1/" }, Token);

        written.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("FluentCms:Ai:AzureOpenAi:ApiKey", "a-value")]
    [InlineData("FluentCms:Ai:OpenAiCompatible:ApiKey", "a-value")]
    [InlineData("FluentCms:Ai:OpenAiCompatible:Endpoint", "not-a-url")]
    [InlineData("FluentCms:Ai:OpenAi:Endpoint", "https://user:a-value@models.example.com/v1")]
    public void AKeyInTheConfigurationWithoutTheEndpointItIsForStopsTheHost(string key, string value)
    {
        using var refused = OptionsOf(new() { [key] = value });
        using var openAi = OptionsOf(new() { ["FluentCms:Ai:OpenAi:ApiKey"] = "a-value" });
        using var bound = OptionsOf(new() { ["FluentCms:Ai:AzureOpenAi:ApiKey"] = "a-value", ["FluentCms:Ai:AzureOpenAi:Endpoint"] = "https://models.example.com/v1" });

        var failure = Should.Throw<OptionsValidationException>(() => refused.GetRequiredService<IOptions<AiOptions>>().Value);
        failure.Message.ShouldContain("Endpoint");
        failure.Message.ShouldNotContain("a-value");
        openAi.GetRequiredService<IOptions<AiOptions>>().Value.ShouldNotBeNull();
        bound.GetRequiredService<IOptions<AiOptions>>().Value.ShouldNotBeNull();
    }

    // The time a call may take is the plugin's. A timeout of the client's own, even of the same length, races it, and a
    // call that passed requestTimeout then ended as a failure of the provider (FCMS1689) rather than a timeout (FCMS1688).
    // A timeout of the client would end the call on the real clock, so the call must still be open a little past
    // requestTimeout, at its least of one second, and end only when the caller cancels it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheClientEndsASlowCallOnlyWhenItsCallerCancelsIt(bool stream)
    {
        await using var stub = await StubProvider.StartAsync();
        stub.Pause = TimeSpan.FromSeconds(30);
        using var factory = Factory([stub.Url("v1")], []);
        var settings = new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = stub.Url("v1"), RequestTimeout = 1 };
        var client = await factory.CreateAsync(Site, settings, Token);
        List<ChatMessage> messages = [new(ChatRole.User, "Ships are big boats.")];
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var received = stub.Received;

        var call = Task.Run(
            async () =>
            {
                if (stream)
                {
                    await foreach (var _ in client!.Client.GetStreamingResponseAsync(messages, cancellationToken: caller.Token))
                    {
                    }
                }
                else
                {
                    await client!.Client.GetResponseAsync(messages, cancellationToken: caller.Token);
                }
            },
            Token);
        await Task.WhenAny(received, call).WaitAsync(Token);
        await Task.Delay(TimeSpan.FromSeconds(settings.RequestTimeout) + TimeSpan.FromMilliseconds(250), Token);
        call.IsCompleted.ShouldBeFalse();

        await caller.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => call);
        stub.Requests.ShouldHaveSingleItem();
    }

    // The cap is max_completion_tokens for every provider. Ollama's /v1 reads only max_tokens, so a site that turns
    // legacyMaxTokens on sends that one alone, plain and streamed. A call without a cap sends neither.
    [Theory]
    [InlineData(AiProvider.OpenAiCompatible, false)]
    [InlineData(AiProvider.AzureOpenAi, false)]
    [InlineData(AiProvider.OpenAi, false)]
    [InlineData(AiProvider.OpenAiCompatible, true)]
    [InlineData(AiProvider.OpenAi, true)]
    public async Task TheCapIsMaxCompletionTokensUnlessTheSiteAsksForTheOlderMaxTokens(AiProvider provider, bool legacy)
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], []);
        var settings = new AiSettings { Provider = provider, Model = "a-model", Endpoint = stub.Url("v1"), ApiKey = "key-of-the-site", LegacyMaxTokens = legacy };
        var client = await factory.CreateAsync(Site, settings, Token);
        List<ChatMessage> messages = [new(ChatRole.User, "Ships are big boats.")];
        var capped = new ChatOptions { MaxOutputTokens = 300 };

        await client!.Client.GetResponseAsync(messages, capped, Token);
        await foreach (var _ in client.Client.GetStreamingResponseAsync(messages, capped, Token))
        {
        }

        await client.Client.GetResponseAsync(messages, cancellationToken: Token);

        var sent = stub.Requests.Select(request => request.Body!).ToList();
        sent.Count.ShouldBe(3);
        foreach (var body in sent[..2])
        {
            (body["max_completion_tokens"]?.GetValue<int>()).ShouldBe(legacy ? null : 300);
            (body["max_tokens"]?.GetValue<int>()).ShouldBe(legacy ? 300 : null);
        }

        sent[2]["max_completion_tokens"].ShouldBeNull();
        sent[2]["max_tokens"].ShouldBeNull();
        (capped.MaxOutputTokens, capped.RawRepresentationFactory).ShouldBe((300, null));
    }

    [Fact]
    public async Task ASiteWithNoProviderGetsNoClient()
    {
        using var factory = Factory([], []);

        (await factory.CreateAsync(Site, new AiSettings(), Token)).ShouldBeNull();
    }

    [Theory]
    [InlineData(AiProvider.OpenAi, "a-model", null, null)]
    [InlineData(AiProvider.OpenAi, null, null, "a-key")]
    [InlineData(AiProvider.OpenAi, " ", null, "a-key")]
    [InlineData(AiProvider.AzureOpenAi, "a-deployment", null, "a-key")]
    [InlineData(AiProvider.OpenAiCompatible, "a-model", null, "a-key")]
    [InlineData(AiProvider.OpenAiCompatible, "a-model", null, null)]
    public async Task AProviderWithoutWhatItNeedsIsNotConfigured(AiProvider provider, string? model, string? endpoint, string? key)
    {
        using var factory = Factory([], []);

        var refusal = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = provider, Model = model, Endpoint = endpoint, ApiKey = key }, Token));

        refusal.Code.ShouldBe("FCMS1685");
    }

    [Fact]
    public async Task OpenAiUsesItsOwnEndpointWhenASiteNamesNone()
    {
        using var factory = Factory([], []);

        var client = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", ApiKey = "a-key" }, Token);

        client.ShouldNotBeNull();
        client.Client.GetService<ChatClientMetadata>().ShouldNotBeNull().ProviderUri.ShouldNotBeNull().Host.ShouldBe("api.openai.com");
    }

    // Settings written while the plugin had Anthropic and Ollama still read, so that a site that names one is told what to
    // use instead of following the instance defaults without a word.
    [Theory]
    [InlineData("anthropic")]
    [InlineData("ollama")]
    [InlineData("Ollama")]
    public async Task ARemovedProviderIsRefusedWithWhatToUse(string provider)
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], new() { ["FluentCms:Ai:OpenAiCompatible:ApiKey"] = "key-of-the-configuration" });
        var settings = JsonSerializer.Deserialize<AiSettings>($$"""{ "provider": "{{provider}}", "model": "a-model", "endpoint": "{{stub.Url("v1")}}", "apiKey": "key-of-the-site" }""", SettingsJson)!;

        var refusal = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, settings, Token));

        refusal.Code.ShouldBe("FCMS1692");
        refusal.Message.ShouldContain("openAiCompatible");
        refusal.Message.ShouldContain("/v1");
        refusal.Message.ShouldContain(nameof(IAiClientFactory));
        stub.Requests.ShouldBeEmpty();
    }

    // The provider reads and writes by its name, as core's settings do, and a removed one by the name it had.
    [Fact]
    public void TheProviderOfTheSettingsReadsTheNamesOfTheRemovedProvidersToo()
    {
        AiSettings Read(string provider) => JsonSerializer.Deserialize<AiSettings>($$"""{ "provider": {{provider}} }""", SettingsJson)!;

        Read("\"openAiCompatible\"").Provider.ShouldBe(AiProvider.OpenAiCompatible);
        Read("\"OPENAI\"").Provider.ShouldBe(AiProvider.OpenAi);
        Read("2").Provider.ShouldBe(AiProvider.AzureOpenAi);
        AiProviders.IsRemoved(Read("\"anthropic\"").Provider).ShouldBeTrue();
        AiProviders.IsRemoved(Read("4").Provider).ShouldBeTrue();
        AiProviders.IsRemoved(AiProvider.OpenAiCompatible).ShouldBeFalse();
        JsonSerializer.Serialize(Read("\"ollama\""), SettingsJson).ShouldContain("\"provider\":\"ollama\"");
        JsonSerializer.Serialize(new AiSettings { Provider = AiProvider.AzureOpenAi }, SettingsJson).ShouldContain("\"provider\":\"azureOpenAi\"");
        Should.Throw<JsonException>(() => Read("\"gemini\""));
        Should.Throw<JsonException>(() => Read("null"));
    }

    [Theory]
    [InlineData("FluentCms:Ai:Anthropic:ApiKey")]
    [InlineData("FluentCms:Ai:ollama:ApiKey")]
    [InlineData("FluentCms:Ai:Ollama:Endpoint")]
    public void ConfigurationThatNamesARemovedProviderStopsTheHost(string key)
    {
        using var refused = Services(new() { [key] = "a-value" });
        using var kept = Services(new() { ["FluentCms:Ai:OpenAiCompatible:ApiKey"] = "a-value", ["FluentCms:Ai:OpenAiCompatible:Endpoint"] = "https://models.example.com/v1" });

        var failure = Should.Throw<OptionsValidationException>(() => refused.GetRequiredService<IOptions<AiOptions>>().Value);
        failure.Message.ShouldContain("FCMS1692");
        failure.Message.ShouldContain("openAiCompatible");
        failure.Message.ShouldContain(nameof(IAiClientFactory));
        failure.Message.ShouldNotContain("a-value");
        kept.GetRequiredService<IOptions<AiOptions>>().Value.ShouldNotBeNull();

        static ServiceProvider Services(Dictionary<string, string?> configuration)
        {
            var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
            AiAssistPlugin.AddOptions(services);
            return services.BuildServiceProvider();
        }
    }

    [Theory]
    [InlineData("https://models.example.com/v1", true)]
    [InlineData("https://models.example.com/v1/", true)]
    [InlineData("https://MODELS.example.com:443/v1", true)]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://localhost:11434/", true)]
    [InlineData("http://models.example.com/v1", false)]
    [InlineData("https://models.example.com/v2", false)]
    [InlineData("https://models.example.com/v1/extra", false)]
    [InlineData("https://models.example.com/V1", false)]
    [InlineData("https://models.example.com:8443/v1", false)]
    [InlineData("https://models.example.com.attacker.example/v1", false)]
    [InlineData("https://attacker.example/models.example.com/v1", false)]
    [InlineData("http://localhost:11435", false)]
    [InlineData("http://127.0.0.1:11434", false)]
    [InlineData("http://169.254.169.254/latest/meta-data", false)]
    public void AnEndpointIsAllowedOnlyWhenItIsOneOfTheList(string endpoint, bool allowed)
    {
        string[] list = ["https://models.example.com/v1", "http://localhost:11434"];

        AiProviderEndpoints.IsAllowed(AiProviderEndpoints.Parse(endpoint)!, list).ShouldBe(allowed);
    }

    [Theory]
    [InlineData("https://user:secret@models.example.com/v1")]
    [InlineData("https://models.example.com/v1?key=1")]
    [InlineData("https://models.example.com/v1#part")]
    [InlineData("ftp://models.example.com/v1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//models.example.com/v1")]
    [InlineData("models.example.com")]
    [InlineData("")]
    public async Task AnEndpointThatIsNoPlainHttpUrlIsRefusedEvenWhenTheListHoldsIt(string endpoint)
    {
        AiProviderEndpoints.Parse(endpoint).ShouldBeNull();
        new AiOptions { AllowedEndpoints = { "https://models.example.com/v1", endpoint } }.IsValid().ShouldBeFalse();
        if (endpoint.Length == 0)
        {
            return;
        }

        using var factory = Factory([endpoint], []);
        var refusal = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = endpoint, ApiKey = "a-key" }, Token));

        refusal.Code.ShouldBe("FCMS1684");
    }

    // A site that sets no key reads the key of the instance defaults (spec 4.13), which is the operator's for the provider
    // and the endpoint of the defaults, so it is not a site's to send anywhere else.
    [Fact]
    public async Task AKeyInheritedFromTheInstanceDefaultsGoesToTheProviderAndEndpointOfTheDefaultsOnly()
    {
        await using var theirs = await StubProvider.StartAsync();
        await using var other = await StubProvider.StartAsync();
        var defaults = new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = theirs.Url("v1"), ApiKey = "key-of-the-instance" };
        using var factory = Factory([theirs.Url("v1"), other.Url("v1"), other.Url()], [], defaults);
        List<ChatMessage> messages = [new(ChatRole.User, "Ships are big boats.")];

        var inherited = await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "another-model", Endpoint = theirs.Url("v1/"), ApiKey = "key-of-the-instance" });
        await inherited!.Client.GetResponseAsync(messages, cancellationToken: Token);
        var otherEndpoint = await Should.ThrowAsync<PluginException>(async () => await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-instance" }));
        var otherProvider = await Should.ThrowAsync<PluginException>(async () => await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.AzureOpenAi, Model = "a-model", Endpoint = other.Url(), ApiKey = "key-of-the-instance" }));
        var sameEndpointOtherProvider = await Should.ThrowAsync<PluginException>(async () => await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.AzureOpenAi, Model = "a-model", Endpoint = theirs.Url("v1"), ApiKey = "key-of-the-instance" }));
        var own = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-site" }, Token);
        await own!.Client.GetResponseAsync(messages, cancellationToken: Token);

        // The key of the defaults is the operator's, and only a key the site set itself is its own credential.
        inherited.SiteCredential.ShouldBeFalse();
        own.SiteCredential.ShouldBeTrue();
        otherEndpoint.Code.ShouldBe("FCMS1685");
        otherProvider.Code.ShouldBe("FCMS1685");
        sameEndpointOtherProvider.Code.ShouldBe("FCMS1685");
        otherEndpoint.Message.ShouldNotContain("key-of-the-instance");
        theirs.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe("Bearer key-of-the-instance");
        other.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe("Bearer key-of-the-site");
    }

    // After the defaults' key is replaced, a site that inherits it reads the new one and is held to the defaults just the
    // same, whatever key it was given; a key that a site set itself is its own, even one that equals the key of the defaults.
    [Fact]
    public async Task WhetherAKeyIsInheritedIsWhatTheSettingsSayAndNotWhatItEquals()
    {
        await using var theirs = await StubProvider.StartAsync();
        await using var other = await StubProvider.StartAsync();
        var defaults = new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = theirs.Url("v1"), ApiKey = "key-of-the-instance" };
        using var factory = Factory([theirs.Url("v1"), other.Url("v1")], [], defaults);

        var replaced = await Should.ThrowAsync<PluginException>(async () => await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-that-was-replaced" }));
        var current = await Should.ThrowAsync<PluginException>(async () => await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-instance" }));
        var setItself = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-instance" }, Token);
        await setItself!.Client.GetResponseAsync([new(ChatRole.User, "Ships are big boats.")], cancellationToken: Token);

        replaced.Code.ShouldBe("FCMS1685");
        current.Code.ShouldBe("FCMS1685");
        theirs.Requests.ShouldBeEmpty();
        other.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe("Bearer key-of-the-instance");
    }

    // The settings the factory is given were read before it asks. A key that the site inherited then is not its own because
    // the site has set another key since: only a key the site holds as its own now goes to an endpoint of its choosing.
    [Fact]
    public async Task AKeyIsTheSitesOwnOnlyWhenTheSiteHoldsThatKeyAsItsOwn()
    {
        await using var theirs = await StubProvider.StartAsync();
        await using var other = await StubProvider.StartAsync();
        var defaults = new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = theirs.Url("v1"), ApiKey = "key-of-the-instance" };
        using var factory = Factory([theirs.Url("v1"), other.Url("v1")], [], defaults);
        var readEarlier = new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-instance" };
        factory.Store.Site = new ResolvedSettings<AiSettings>(new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-site" }, []);

        var refusal = await Should.ThrowAsync<PluginException>(async () => await factory.Inner.CreateAsync(Site, readEarlier, Token));

        refusal.Code.ShouldBe("FCMS1685");
        other.Requests.ShouldBeEmpty();
        theirs.Requests.ShouldBeEmpty();
    }

    // The provider's own endpoint is an endpoint like any other: a key the defaults hold for OpenAI stays there.
    [Fact]
    public async Task AKeyInheritedForAProvidersOwnEndpointIsNotSentToAnEndpointASiteNames()
    {
        await using var other = await StubProvider.StartAsync();
        var defaults = new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", ApiKey = "key-of-the-instance" };
        using var factory = Factory([other.Url("v1")], [], defaults);

        var atItsOwn = await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.OpenAi, Model = "another-model", ApiKey = "key-of-the-instance" });
        var elsewhere = await Should.ThrowAsync<PluginException>(async () => await factory.CreateInheritingAsync(new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-instance" }));

        atItsOwn.ShouldNotBeNull();
        elsewhere.Code.ShouldBe("FCMS1685");
        other.Requests.ShouldBeEmpty();
    }

    private static ServiceProvider OptionsOf(Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        AiAssistPlugin.AddOptions(services);
        return services.BuildServiceProvider();
    }

    private static TestFactory Factory(string[] allowed, Dictionary<string, string?> configuration, AiSettings? defaults = null)
    {
        var options = new AiOptions();
        foreach (var endpoint in allowed)
        {
            options.AllowedEndpoints.Add(endpoint);
        }

        var store = new Store(defaults ?? new AiSettings());
        var services = new ServiceCollection().AddSingleton<ISettings<AiSettings>>(store).BuildServiceProvider();
        return new TestFactory(
            new AiClientFactory(
                Options.Create(options),
                new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(),
                services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AiClientFactory>.Instance),
            store);
    }

    // The default factory, given settings as the site holds them: the key is the site's own unless the test says the site
    // inherits it.
    private sealed class TestFactory(AiClientFactory inner, Store store) : IDisposable
    {
        public AiClientFactory Inner => inner;

        public Store Store => store;

        public ValueTask<AiClient?> CreateAsync(SiteId site, AiSettings settings, CancellationToken cancellationToken)
        {
            store.Site = new ResolvedSettings<AiSettings>(settings, []);
            return inner.CreateAsync(site, settings, cancellationToken);
        }

        public ValueTask<AiClient?> CreateInheritingAsync(AiSettings settings)
        {
            store.Site = new ResolvedSettings<AiSettings>(settings, [nameof(AiSettings.ApiKey)]);
            return inner.CreateAsync(Site, settings, Token);
        }

        public void Dispose() => inner.Dispose();
    }

    // What the factory reads of the settings area: the instance defaults, and the settings of the site as they are now.
    private sealed class Store(AiSettings defaults) : ISettings<AiSettings>
    {
        public ResolvedSettings<AiSettings>? Site { get; set; }

        public ValueTask<AiSettings> GetDefaultAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(defaults);

        public ValueTask<ResolvedSettings<AiSettings>> ResolveAsync(SiteId site, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Site ?? throw new InvalidOperationException("The test did not say what the site holds."));

        public ValueTask<AiSettings> GetAsync(SiteId site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SetAsync(SiteId site, AiSettings value, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SetDefaultAsync(AiSettings value, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ResetAsync(SiteId site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ResetDefaultAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
