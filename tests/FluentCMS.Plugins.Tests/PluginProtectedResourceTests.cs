using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

// What MapProtectedResource gives a plugin's group (#2977): the origin it names, a caller the FluentCMS scheme signed in
// with the scope of the resource, the challenge of a refusal, and the metadata documents core serves under /.well-known.
public sealed class PluginProtectedResourceTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheGroupTakesAKeyOrATokenWithItsScopeAndNoEndpointIsAnonymous()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<GuardedPlugin>());
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        using var anonymous = host.CreateSecureClient();
        using var key = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.PublicName, cancellationToken: Token));
        using var scoped = await host.AsUserAsync(user, "openid", "mcp");
        using var unscoped = await host.AsUserAsync(user, "openid", "management");
        var origin = new Uri("/guarded/origin", UriKind.Relative);

        using var refused = await anonymous.GetAsync(origin, Token);
        using var byKey = await key.GetAsync(origin, Token);
        using var byToken = await scoped.GetAsync(origin, Token);
        using var withoutScope = await unscoped.GetAsync(origin, Token);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refused.Headers.WwwAuthenticate.ShouldHaveSingleItem().Parameter.ShouldBe("resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/guarded\", scope=\"mcp\"");
        byKey.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await byKey.Content.ReadAsStringAsync(Token)).ShouldBe(nameof(Origin.Api));
        byToken.StatusCode.ShouldBe(HttpStatusCode.OK);
        withoutScope.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        withoutScope.Headers.WwwAuthenticate.ShouldHaveSingleItem().Parameter!.ShouldStartWith("error=\"insufficient_scope\", scope=\"mcp\"");
    }

    [Fact]
    public async Task TheOperationsOfTheGroupEnterWithTheOriginThePluginNames()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<GuardedPlugin>(guarded => guarded.Origin = Origin.Mcp));
        using var key = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

        (await key.GetStringAsync(new Uri("/guarded/origin", UriKind.Relative), Token)).ShouldBe(nameof(Origin.Mcp));
    }

    [Fact]
    public async Task CoreServesTheMetadataOfTheResourceAndTheOneWithoutAPathWhileThereIsOne()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<GuardedPlugin>());
        using var client = host.CreateSecureClient();

        var own = JsonNode.Parse(await client.GetStringAsync(new Uri("/.well-known/oauth-protected-resource/guarded", UriKind.Relative), Token))!;
        var root = JsonNode.Parse(await client.GetStringAsync(new Uri("/.well-known/oauth-protected-resource", UriKind.Relative), Token))!;

        own["resource"]!.GetValue<string>().ShouldBe("https://localhost/guarded");
        own["resource_name"]!.GetValue<string>().ShouldBe("Guarded");
        own["scopes_supported"]!.AsArray().Select(scope => scope!.GetValue<string>()).ShouldBe(["mcp"]);
        root.ToJsonString().ShouldBe(own.ToJsonString());
    }

    [Fact]
    public async Task WithSeveralResourcesEachHasItsMetadataAndTheOneWithoutAPathIsNotFound()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<GuardedPlugin>(guarded => guarded.Second = "/vault"));
        using var client = host.CreateSecureClient();

        using var root = await client.GetAsync(new Uri("/.well-known/oauth-protected-resource", UriKind.Relative), Token);
        var first = JsonNode.Parse(await client.GetStringAsync(new Uri("/.well-known/oauth-protected-resource/guarded", UriKind.Relative), Token))!;
        var second = JsonNode.Parse(await client.GetStringAsync(new Uri("/.well-known/oauth-protected-resource/vault", UriKind.Relative), Token))!;

        root.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        JsonNode.Parse(await root.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>().ShouldBe("FCMS3104");
        first["resource"]!.GetValue<string>().ShouldBe("https://localhost/guarded");
        second["resource"]!.GetValue<string>().ShouldBe("https://localhost/vault");
    }

    [Theory]
    [InlineData("/api/management/v1/guarded")]
    [InlineData("/.well-known/guarded")]
    [InlineData("/guarded/{tenant}")]
    [InlineData("/{tenant}")]
    [InlineData("guarded")]
    [InlineData("/guarded/")]
    public async Task APrefixThatIsNoFixedPathOfThePluginsOwnStopsTheHost(string prefix)
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<GuardedPlugin>(guarded => guarded.Prefix = prefix));

        var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

        exception.Code.ShouldBe("FCMS1524");
        exception.Message.ShouldContain("'guarded'");
    }

    [Theory]
    [InlineData("management", Origin.Api, null)]
    [InlineData("nothing", Origin.Api, null)]
    [InlineData("mcp", Origin.Job, null)]
    [InlineData("mcp", Origin.Admin, null)]
    [InlineData("mcp", Origin.Api, "/Guarded")]
    public async Task AScopeTheServerDoesNotOfferAnOriginOfTheSystemOrAResourceMappedTwiceStopsTheHost(string scope, Origin origin, string? second)
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<GuardedPlugin>(guarded => (guarded.Scope, guarded.Origin, guarded.Second) = (scope, origin, second)));

        var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

        exception.Code.ShouldBe("FCMS1527");
        exception.Message.ShouldContain("'guarded'");
    }
}
