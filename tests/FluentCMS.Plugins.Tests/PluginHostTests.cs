using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Content;
using FluentCMS.Core.Plugins;
using FluentCMS.Hooks;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

public sealed class PluginHostTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task APluginOfAReferencedAssemblyStartsWithoutBeingAdded()
    {
        await using var host = new FluentCmsTestHost();

        var greeting = host.Services.GetRequiredService<SampleGreeting>();

        greeting.Text.ShouldBe("discovered");
        greeting.Configured.ShouldBeTrue();
        var sample = host.Services.GetRequiredService<PluginRegistry>().Plugins.ShouldHaveSingleItem();
        sample.Name.ShouldBe("sample");
        sample.Source.ShouldBe(PluginSource.Discovered);
    }

    [Fact]
    public async Task AnAddedPluginStarts()
    {
        var calls = new PluginCalls();
        await using var host = Host(calls).Configure(cms => cms.AddPlugin<AlphaPlugin>());

        _ = host.Services;

        calls.All.ShouldContain("alpha.services");
        calls.All.ShouldContain("alpha.configure");
        host.Services.GetRequiredService<PluginRegistry>().Plugins.Single(plugin => plugin.Name == "alpha").Source.ShouldBe(PluginSource.Explicit);
    }

    [Fact]
    public async Task APluginFoundBothWaysIsRegisteredOnceWithTheExplicitConfigurationAndPlace()
    {
        var calls = new PluginCalls();
        await using var host = Host(calls).Configure(cms => cms
            .AddPlugin<SamplePlugin>(sample => sample.Greeting = "explicit")
            .AddPlugin<AlphaPlugin>());

        host.Services.GetServices<SampleGreeting>().ShouldHaveSingleItem().Text.ShouldBe("explicit");
        calls.All.Count(call => call == "sample.services").ShouldBe(1);
        calls.All.Count(call => call == "sample.configure").ShouldBe(1);
        var plugins = host.Services.GetRequiredService<PluginRegistry>().Plugins;
        plugins.Select(plugin => plugin.Name).ShouldBe(["sample", "alpha"]);
        plugins[0].Source.ShouldBe(PluginSource.Explicit);
    }

    [Fact]
    public async Task DependenciesRunFirstAndOtherwiseAddedPluginsKeepTheirOrderAheadOfDiscoveredOnes()
    {
        var calls = new PluginCalls();
        await using var host = Host(calls).Configure(cms => cms
            .AddPlugin<BetaPlugin>()
            .AddPlugin<AlphaPlugin>()
            .AddPlugin<GammaPlugin>());

        _ = host.Services;

        string[] order = ["gamma", "beta", "alpha", "sample"];
        calls.All.Where(call => call.EndsWith(".services", StringComparison.Ordinal)).ShouldBe(order.Select(name => $"{name}.services"));
        calls.All.Where(call => call.EndsWith(".configure", StringComparison.Ordinal)).ShouldBe(order.Select(name => $"{name}.configure"));
        calls.All.ToList().FindLastIndex(call => call.EndsWith(".services", StringComparison.Ordinal))
            .ShouldBeLessThan(calls.All.ToList().FindIndex(call => call.EndsWith(".configure", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ADiscoveredPluginThatAnAddedOneDependsOnMovesAheadOfIt()
    {
        var calls = new PluginCalls();
        await using var host = Host(calls).Configure(cms => cms
            .AddPlugin<NeedsSamplePlugin>()
            .AddPlugin<AlphaPlugin>());

        _ = host.Services;

        host.Services.GetRequiredService<PluginRegistry>().Plugins.Select(plugin => plugin.Name).ShouldBe(["sample", "needs-sample", "alpha"]);
        calls.All.Where(call => call.EndsWith(".services", StringComparison.Ordinal)).ShouldBe(["sample.services", "needs-sample.services", "alpha.services"]);
    }

    // Unsorted, the host's hook comes first (registered first, for every type) and stamp's last (for the content class).
    [Fact]
    public async Task HooksOfEqualOrderRunInPluginDependencyOrderAndAheadOfTheHostsOwn()
    {
        var calls = new PluginCalls();
        await using var host = Host(calls)
            .WithContentTypes<Note>()
            .WithServices(services => services.AddScoped<IBeforeCreateHook<ContentDocument>, HostHook>())
            .Configure(cms => cms
                .AddPlugin<AuditPlugin>()
                .AddPlugin<StampPlugin>());
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        await host.As(editor).Content<Note>().CreateAsync(new Note { Title = "ordered" }, Token);

        calls.All.Where(call => call.EndsWith(".hook", StringComparison.Ordinal)).ShouldBe(["stamp.hook", "audit.hook", "sample.hook", "host.hook"]);
    }

    [Fact]
    public async Task TheStartupReportAndDiagnosticsListEachPluginWithItsNameAndVersion()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.AddPlugin<BetaPlugin>().AddPlugin<GammaPlugin>().AddPlugin<AlphaPlugin>());
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);

        (string, string)[] expected = [("gamma", "3.0.0"), ("beta", "2.0.0"), ("alpha", StartupReportFactory.VersionOf(typeof(AlphaPlugin).Assembly)), ("sample", "1.2.0")];
        host.Services.GetRequiredService<StartupReportHolder>().Current!.Plugins.Select(plugin => (plugin.Name, plugin.Version)).ShouldBe(expected);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var served = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["plugins"]!.AsArray();
        served.Select(plugin => (plugin!["name"]!.GetValue<string>(), plugin["version"]!.GetValue<string>())).ShouldBe(expected);
    }

    private static FluentCmsTestHost Host(PluginCalls calls) => new FluentCmsTestHost().WithServices(services => services.AddSingleton(calls));
}
