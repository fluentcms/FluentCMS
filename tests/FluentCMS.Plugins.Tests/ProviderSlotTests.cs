using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Caching;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Identity;
using FluentCMS.Data.Sqlite;
using FluentCMS.Providers;
using FluentCMS.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

public sealed class ProviderSlotTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithNothingConfiguredEachSlotResolvesToItsCoreDefaultAndTheReportNamesIt()
    {
        await using var host = new FluentCmsTestHost();
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        host.Services.GetRequiredService<IDistributedCache>().ShouldBeOfType<MemoryDistributedCache>();
        host.Services.GetRequiredService<IEmailSender>().ShouldBeOfType<LogEmailSender>();
        host.Services.GetRequiredService<IDistributedLock>().ShouldBeOfType<DatabaseDistributedLock>();
        host.Services.GetRequiredService<ISearchIndex>().ShouldBeOfType<NoSearchIndex>();

        // The framework's in-memory output cache store is internal, so it is known by its name.
        var store = host.Services.GetRequiredService<IOutputCacheStore>().GetType().FullName!;
        store.ShouldBe("Microsoft.AspNetCore.OutputCaching.Memory.MemoryOutputCacheStore");

        (string, string, string)[] expected =
        [
            (typeof(IEmailSender).FullName!, typeof(LogEmailSender).FullName!, "default"),
            (typeof(IDistributedLock).FullName!, typeof(DatabaseDistributedLock).FullName!, "default"),
            (typeof(IDistributedCache).FullName!, typeof(MemoryDistributedCache).FullName!, "default"),
            (typeof(IOutputCacheStore).FullName!, store, "default"),
            (typeof(ISearchIndex).FullName!, typeof(NoSearchIndex).FullName!, "default"),
        ];
        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var served = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["providerSlots"]!.AsArray();
        served.Select(slot => (slot!["slot"]!.GetValue<string>(), slot["implementation"]!.GetValue<string>(), slot["source"]!.GetValue<string>())).ShouldBe(expected);
        host.Services.GetRequiredService<StartupReportFactory>().CreateDegraded(new InvalidOperationException()).ProviderSlots
            .Select(slot => (slot.Slot, slot.Implementation!, slot.Source)).ShouldBe(expected);
    }

    [Fact]
    public async Task TheHostReplacesEveryCoreSlotThroughTheSlotApi()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => cms.Providers
            .Use<IEmailSender, HostSender>()
            .Use<IDistributedLock, HostLock>()
            .Use<IDistributedCache, HostCache>()
            .Use<IOutputCacheStore, HostOutputCacheStore>()
            .Use<ISearchIndex, HostSearchIndex>());

        host.Services.GetRequiredService<IEmailSender>().ShouldBeOfType<HostSender>();
        host.Services.GetRequiredService<IDistributedLock>().ShouldBeOfType<HostLock>();
        host.Services.GetRequiredService<IDistributedCache>().ShouldBeOfType<HostCache>();
        host.Services.GetRequiredService<IOutputCacheStore>().ShouldBeOfType<HostOutputCacheStore>();
        host.Services.GetRequiredService<ISearchIndex>().ShouldBeOfType<HostSearchIndex>();
        Report(host).Select(slot => (slot.Implementation, slot.Source)).ShouldBe(
        [
            (typeof(HostSender).FullName, "host"),
            (typeof(HostLock).FullName, "host"),
            (typeof(HostCache).FullName, "host"),
            (typeof(HostOutputCacheStore).FullName, "host"),
            (typeof(HostSearchIndex).FullName, "host"),
        ]);
    }

    [Fact]
    public async Task APluginsReplacementWinsOverRegistrationsOfTheSlotWhereverTheyAreMade()
    {
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton<IEmailSender, HostSender>())
            .Configure(cms =>
            {
                cms.Services.AddSingleton<IEmailSender, HostSender>();
                Add<FirstSlotsPlugin>(cms, register: services => services.AddSingleton<IEmailSender, OtherSender>());
                Add<SecondSlotsPlugin>(cms, replace: slots => slots.Use<IEmailSender, PluginSender>());
            });

        host.Services.GetServices<IEmailSender>().ShouldHaveSingleItem().ShouldBeOfType<PluginSender>();
        Report(host)[0].ShouldBe(new() { Slot = typeof(IEmailSender).FullName!, Implementation = typeof(PluginSender).FullName, Source = "plugin", Plugin = "second-slots" });
    }

    [Fact]
    public async Task AServiceThatAPluginRegistersForTheSlotDirectlyWinsOverTheDefault()
    {
        await using var host = new FluentCmsTestHost().Configure(cms => Add<FirstSlotsPlugin>(cms, register: services => services.AddSingleton<IDistributedCache, PluginCache>()));

        host.Services.GetRequiredService<IDistributedCache>().ShouldBeOfType<PluginCache>();
        Report(host)[2].ShouldBe(new() { Slot = typeof(IDistributedCache).FullName!, Implementation = typeof(PluginCache).FullName, Source = "services" });
    }

    [Fact]
    public async Task TheHostsReplacementWinsOverAPluginsWhereverEachIsMade()
    {
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton<IDistributedCache, OtherCache>())
            .Configure(cms =>
            {
                Add<FirstSlotsPlugin>(cms, replace: slots => slots.Use<IDistributedCache, PluginCache>());
                cms.Providers.Use<IDistributedCache, HostCache>();
                cms.Services.AddSingleton<IDistributedCache, OtherCache>();
            });

        host.Services.GetServices<IDistributedCache>().ShouldHaveSingleItem().ShouldBeOfType<HostCache>();
        Report(host)[2].Source.ShouldBe("host");
    }

    [Fact]
    public void TwoPluginsThatReplaceOneSlotStopTheHostNamingBoth()
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => Compose(cms =>
        {
            Add<FirstSlotsPlugin>(cms, replace: slots => slots.Use<IEmailSender, PluginSender>());
            Add<SecondSlotsPlugin>(cms, replace: slots => slots.Use<IEmailSender, OtherSender>());
        }));

        exception.Code.ShouldBe("FCMS1540");
        exception.Message.ShouldContain("'first-slots'");
        exception.Message.ShouldContain("'second-slots'");
        exception.Message.ShouldContain(typeof(IEmailSender).FullName!);
    }

    [Fact]
    public void TheHostsChoiceSettlesTwoPluginsThatReplaceOneSlot()
    {
        var services = Compose(cms =>
        {
            Add<FirstSlotsPlugin>(cms, replace: slots => slots.Use<IEmailSender, PluginSender>());
            Add<SecondSlotsPlugin>(cms, replace: slots => slots.Use<IEmailSender, OtherSender>());
            cms.Providers.Use<IEmailSender, HostSender>();
        });

        services.Where(descriptor => descriptor.ServiceType == typeof(IEmailSender)).ShouldHaveSingleItem().ImplementationType.ShouldBe(typeof(HostSender));
    }

    [Fact]
    public void ReplacingATypeThatIsNotASlotStopsTheHost()
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => Compose(cms => cms.Providers.Use<TimeProvider, FrozenTime>()));

        exception.Code.ShouldBe("FCMS1541");
        exception.Message.ShouldContain(typeof(TimeProvider).FullName!);
    }

    private static IReadOnlyList<FluentCMS.Diagnostics.StartupProviderSlot> Report(FluentCmsTestHost host) =>
        host.Services.GetRequiredService<StartupReportHolder>().Current!.ProviderSlots;

    private static ServiceCollection Compose(Action<FluentCmsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddFluentCms(cms =>
        {
            cms.UseSqlite("Data Source=:memory:");
            configure(cms);
        });
        return services;
    }

    private static void Add<TPlugin>(FluentCmsBuilder cms, Action<IProviderSlots>? replace = null, Action<IServiceCollection>? register = null)
        where TPlugin : SlotsPlugin, new() =>
        cms.AddPlugin<TPlugin>(plugin =>
        {
            plugin.Replace = replace;
            plugin.Register = register;
        });

    public abstract class SlotsPlugin : IPlugin
    {
        public Action<IProviderSlots>? Replace { get; set; }

        public Action<IServiceCollection>? Register { get; set; }

        public void ConfigureServices(IPluginServiceBuilder plugin)
        {
            Register?.Invoke(plugin.Services);
            Replace?.Invoke(plugin.Providers);
        }

        public void Configure(IPluginAppBuilder app)
        {
        }
    }

    [FluentCmsPlugin("first-slots", SdkRange = "[1.0.0,2.0.0)")]
    public sealed class FirstSlotsPlugin : SlotsPlugin;

    [FluentCmsPlugin("second-slots", SdkRange = "[1.0.0,2.0.0)")]
    public sealed class SecondSlotsPlugin : SlotsPlugin;

    public class HostSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public sealed class PluginSender : HostSender;

    public sealed class OtherSender : HostSender;

    public sealed class HostCache(IOptions<MemoryDistributedCacheOptions> options) : MemoryDistributedCache(options);

    public sealed class PluginCache(IOptions<MemoryDistributedCacheOptions> options) : MemoryDistributedCache(options);

    public sealed class OtherCache(IOptions<MemoryDistributedCacheOptions> options) : MemoryDistributedCache(options);

    public sealed class FrozenTime : TimeProvider;

    public sealed class HostOutputCacheStore : IOutputCacheStore
    {
        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    public sealed class HostSearchIndex : ISearchIndex
    {
        public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    public sealed class HostLock : IDistributedLock
    {
        public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDistributedLockHandle?>(new Handle(name));

        private sealed class Handle(string name) : IDistributedLockHandle
        {
            public string Name => name;

            public CancellationToken Lost => CancellationToken.None;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
