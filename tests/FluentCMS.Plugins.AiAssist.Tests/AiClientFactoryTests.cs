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

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AKeyThatTheSettingsDoNotHoldComesFromTheConfigurationOfItsProvider()
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], new() { ["FluentCms:Ai:OpenAiCompatible:ApiKey"] = "key-of-the-configuration", ["FluentCms:Ai:Anthropic:ApiKey"] = "key-of-another-provider" });
        List<ChatMessage> messages = [new(ChatRole.User, "Ships are big boats.")];

        var fromConfiguration = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = stub.Url("v1") }, Token);
        await fromConfiguration!.Client.GetResponseAsync(messages, cancellationToken: Token);
        var fromSettings = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = stub.Url("v1"), ApiKey = "key-of-the-site" }, Token);
        await fromSettings!.Client.GetResponseAsync(messages, cancellationToken: Token);

        (fromConfiguration.Provider, fromConfiguration.Model).ShouldBe(("openAiCompatible", "a-model"));
        stub.Requests.Select(request => request.Headers["Authorization"]).ShouldBe(["Bearer key-of-the-configuration", "Bearer key-of-the-site"]);
    }

    // The operator's key for OpenAI or Anthropic is not a site's to send to another endpoint, even an allowed one.
    [Theory]
    [InlineData(AiProvider.OpenAi)]
    [InlineData(AiProvider.Anthropic)]
    public async Task TheConfigurationsKeyForOpenAiOrAnthropicGoesToTheProvidersOwnEndpointOnly(AiProvider provider)
    {
        await using var stub = await StubProvider.StartAsync();
        using var factory = Factory([stub.Url("v1")], new() { [$"FluentCms:Ai:{provider}:ApiKey"] = "key-of-the-operator" });

        var atItsOwn = await factory.CreateAsync(Site, new AiSettings { Provider = provider, Model = "a-model" }, Token);
        var elsewhere = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = provider, Model = "a-model", Endpoint = stub.Url("v1") }, Token));
        var withItsOwnKey = await factory.CreateAsync(Site, new AiSettings { Provider = provider, Model = "a-model", Endpoint = stub.Url("v1"), ApiKey = "key-of-the-site" }, Token);

        atItsOwn.ShouldNotBeNull();
        elsewhere.Code.ShouldBe("FCMS1685");
        withItsOwnKey.ShouldNotBeNull();
        stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASiteWithNoProviderGetsNoClient()
    {
        using var factory = Factory([], []);

        (await factory.CreateAsync(Site, new AiSettings(), Token)).ShouldBeNull();
    }

    [Theory]
    [InlineData(AiProvider.OpenAi, "a-model", null, null)]
    [InlineData(AiProvider.Anthropic, "a-model", null, null)]
    [InlineData(AiProvider.OpenAi, null, null, "a-key")]
    [InlineData(AiProvider.OpenAi, " ", null, "a-key")]
    [InlineData(AiProvider.AzureOpenAi, "a-deployment", null, "a-key")]
    [InlineData(AiProvider.OpenAiCompatible, "a-model", null, "a-key")]
    [InlineData(AiProvider.Ollama, "a-model", null, null)]
    public async Task AProviderWithoutWhatItNeedsIsNotConfigured(AiProvider provider, string? model, string? endpoint, string? key)
    {
        using var factory = Factory([], []);

        var refusal = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = provider, Model = model, Endpoint = endpoint, ApiKey = key }, Token));

        refusal.Code.ShouldBe("FCMS1685");
    }

    [Theory]
    [InlineData(AiProvider.OpenAi)]
    [InlineData(AiProvider.Anthropic)]
    public async Task OpenAiAndAnthropicUseTheirOwnEndpointsWhenASiteNamesNone(AiProvider provider)
    {
        using var factory = Factory([], []);

        var client = await factory.CreateAsync(Site, new AiSettings { Provider = provider, Model = "a-model", ApiKey = "a-key" }, Token);

        client.ShouldNotBeNull();
        client.Client.GetService<ChatClientMetadata>().ShouldNotBeNull().ProviderUri.ShouldNotBeNull().Host.ShouldBe(provider == AiProvider.OpenAi ? "api.openai.com" : "api.anthropic.com");
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

    // A site's first save without a key takes the key of the instance defaults with it (spec 4.13). That key is the
    // operator's for the provider and endpoint of the defaults, so it is not a site's to send anywhere else.
    [Fact]
    public async Task AKeyInheritedFromTheInstanceDefaultsGoesToTheProviderAndEndpointOfTheDefaultsOnly()
    {
        await using var theirs = await StubProvider.StartAsync();
        await using var other = await StubProvider.StartAsync();
        var defaults = new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = theirs.Url("v1"), ApiKey = "key-of-the-instance" };
        using var factory = Factory([theirs.Url("v1"), other.Url("v1"), other.Url()], [], defaults);
        List<ChatMessage> messages = [new(ChatRole.User, "Ships are big boats.")];

        var inherited = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "another-model", Endpoint = theirs.Url("v1/"), ApiKey = "key-of-the-instance" }, Token);
        await inherited!.Client.GetResponseAsync(messages, cancellationToken: Token);
        var otherEndpoint = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-instance" }, Token));
        var otherProvider = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.Ollama, Model = "a-model", Endpoint = other.Url(), ApiKey = "key-of-the-instance" }, Token));
        var sameEndpointOtherProvider = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.AzureOpenAi, Model = "a-model", Endpoint = theirs.Url("v1"), ApiKey = "key-of-the-instance" }, Token));
        var own = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAiCompatible, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-site" }, Token);
        await own!.Client.GetResponseAsync(messages, cancellationToken: Token);

        otherEndpoint.Code.ShouldBe("FCMS1685");
        otherProvider.Code.ShouldBe("FCMS1685");
        sameEndpointOtherProvider.Code.ShouldBe("FCMS1685");
        otherEndpoint.Message.ShouldNotContain("key-of-the-instance");
        theirs.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe("Bearer key-of-the-instance");
        other.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe("Bearer key-of-the-site");
    }

    // The provider's own endpoint is an endpoint like any other: a key the defaults hold for OpenAI stays there.
    [Fact]
    public async Task AKeyInheritedForAProvidersOwnEndpointIsNotSentToAnEndpointASiteNames()
    {
        await using var other = await StubProvider.StartAsync();
        var defaults = new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", ApiKey = "key-of-the-instance" };
        using var factory = Factory([other.Url("v1")], [], defaults);

        var atItsOwn = await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "another-model", ApiKey = "key-of-the-instance" }, Token);
        var elsewhere = await Should.ThrowAsync<PluginException>(async () => await factory.CreateAsync(Site, new AiSettings { Provider = AiProvider.OpenAi, Model = "a-model", Endpoint = other.Url("v1"), ApiKey = "key-of-the-instance" }, Token));

        atItsOwn.ShouldNotBeNull();
        elsewhere.Code.ShouldBe("FCMS1685");
        other.Requests.ShouldBeEmpty();
    }

    private static AiClientFactory Factory(string[] allowed, Dictionary<string, string?> configuration, AiSettings? defaults = null)
    {
        var options = new AiOptions();
        foreach (var endpoint in allowed)
        {
            options.AllowedEndpoints.Add(endpoint);
        }

        var services = new ServiceCollection().AddSingleton<ISettings<AiSettings>>(new InstanceDefaults(defaults ?? new AiSettings())).BuildServiceProvider();
        return new AiClientFactory(
            Options.Create(options),
            new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiClientFactory>.Instance);
    }

    // The instance defaults of the settings area, which is all the factory reads of it.
    private sealed class InstanceDefaults(AiSettings defaults) : ISettings<AiSettings>
    {
        public ValueTask<AiSettings> GetDefaultAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(defaults);

        public ValueTask<AiSettings> GetAsync(SiteId site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SetAsync(SiteId site, AiSettings value, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask SetDefaultAsync(AiSettings value, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ResetAsync(SiteId site, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ResetDefaultAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
