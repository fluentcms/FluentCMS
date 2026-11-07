using FluentCMS.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.DependencyInjection;

public sealed class ProvidersBuilderTests
{
    [Fact]
    public void WithNothingChosenTheDefaultFillsTheSlot()
    {
        var (provider, slots) = Apply(new ProvidersBuilder().Default<ISlot, DefaultSlot>());

        provider.GetRequiredService<ISlot>().ShouldBeOfType<DefaultSlot>();
        Describe(provider, slots).ShouldBe((typeof(DefaultSlot).FullName, "default", null));
        slots.Describe(provider).Single().Slot.ShouldBe(typeof(ISlot).FullName);
    }

    [Fact]
    public void AServiceRegisteredForTheSlotDirectlyWinsOverTheDefault()
    {
        var (provider, slots) = Apply(new ProvidersBuilder().Default<ISlot, DefaultSlot>(), services => services.AddSingleton<ISlot, RegisteredSlot>());

        provider.GetRequiredService<ISlot>().ShouldBeOfType<RegisteredSlot>();
        Describe(provider, slots).ShouldBe((typeof(RegisteredSlot).FullName, "services", null));
    }

    [Fact]
    public void ARegistrationOfTheSlotThatCoreMadeGivesWayToTheDefault()
    {
        var services = new ServiceCollection();
        var providers = new ProvidersBuilder().Default<ISlot, DefaultSlot>();
        providers.BeforeCore(services);
        services.AddSingleton<ISlot, RegisteredSlot>();
        var slots = providers.Apply(services);
        using var provider = services.BuildServiceProvider();

        provider.GetServices<ISlot>().ShouldHaveSingleItem().ShouldBeOfType<DefaultSlot>();
        Describe(provider, slots).ShouldBe((typeof(DefaultSlot).FullName, "default", null));
    }

    [Fact]
    public void TheHostsReplacementWinsOverAPluginsAndOverEveryRegistrationOfTheSlot()
    {
        var providers = new ProvidersBuilder().Default<ISlot, DefaultSlot>();
        providers.Use<ISlot, HostSlot>();
        providers.For("first").Use<ISlot, PluginSlot>();

        var (provider, slots) = Apply(providers, services => services.AddSingleton<ISlot, RegisteredSlot>());

        provider.GetServices<ISlot>().ShouldHaveSingleItem().ShouldBeOfType<HostSlot>();
        Describe(provider, slots).ShouldBe((typeof(HostSlot).FullName, "host", null));
    }

    [Fact]
    public void APluginsReplacementWinsOverARegistrationOfTheSlotAndTheDefault()
    {
        var providers = new ProvidersBuilder().Default<ISlot, DefaultSlot>();
        providers.For("first").Use<ISlot, PluginSlot>();

        var (provider, slots) = Apply(providers, services => services.AddSingleton<ISlot, RegisteredSlot>());

        provider.GetServices<ISlot>().ShouldHaveSingleItem().ShouldBeOfType<PluginSlot>();
        Describe(provider, slots).ShouldBe((typeof(PluginSlot).FullName, "plugin", "first"));
    }

    [Fact]
    public void TheLastReplacementOfOneCallerWins()
    {
        var host = new ProvidersBuilder().Default<ISlot, DefaultSlot>().Use<ISlot, PluginSlot>().Use<ISlot, HostSlot>();
        Apply(host).Provider.GetRequiredService<ISlot>().ShouldBeOfType<HostSlot>();

        var plugin = new ProvidersBuilder().Default<ISlot, DefaultSlot>();
        plugin.For("first").Use<ISlot, HostSlot>().Use<ISlot, PluginSlot>();
        plugin.For("first").Use<ISlot, OtherPluginSlot>();
        Apply(plugin).Provider.GetRequiredService<ISlot>().ShouldBeOfType<OtherPluginSlot>();
    }

    [Fact]
    public void TwoPluginsThatReplaceOneSlotStopTheHostNamingBoth()
    {
        var providers = new ProvidersBuilder().Default<ISlot, DefaultSlot>();
        providers.For("first").Use<ISlot, PluginSlot>();
        providers.For("second").Use<ISlot, OtherPluginSlot>();

        var exception = Should.Throw<FluentCmsConfigurationException>(() => providers.Apply(new ServiceCollection()));

        exception.Code.ShouldBe("FCMS1540");
        exception.Message.ShouldContain($"'first' ({typeof(PluginSlot).FullName}) and 'second' ({typeof(OtherPluginSlot).FullName})");
        exception.Message.ShouldContain(typeof(ISlot).FullName!);
        exception.Message.ShouldContain("cms.Providers.Use<ISlot, T>()");
    }

    [Fact]
    public void TheHostsChoiceSettlesTwoPluginsThatReplaceOneSlot()
    {
        var providers = new ProvidersBuilder().Default<ISlot, DefaultSlot>();
        providers.For("first").Use<ISlot, PluginSlot>();
        providers.For("second").Use<ISlot, OtherPluginSlot>();
        providers.Use<ISlot, HostSlot>();

        Apply(providers).Provider.GetRequiredService<ISlot>().ShouldBeOfType<HostSlot>();
    }

    [Fact]
    public void ReplacingATypeThatIsNotASlotStopsTheHost()
    {
        var host = new ProvidersBuilder().Default<ISlot, DefaultSlot>().Use<IOther, Other>();
        var refused = Should.Throw<FluentCmsConfigurationException>(() => host.Apply(new ServiceCollection()));
        refused.Code.ShouldBe("FCMS1541");
        refused.Message.ShouldContain($"The host replaces {typeof(IOther).FullName}");
        refused.Message.ShouldContain(typeof(ISlot).FullName!);

        var plugin = new ProvidersBuilder().Default<ISlot, DefaultSlot>();
        plugin.For("first").Use<IOther, Other>();
        refused = Should.Throw<FluentCmsConfigurationException>(() => plugin.Apply(new ServiceCollection()));
        refused.Code.ShouldBe("FCMS1541");
        refused.Message.ShouldContain($"The plugin 'first' replaces {typeof(IOther).FullName}");
    }

    [Fact]
    public void ARegistrationAfterTheChoiceComesLastAndTheReportSaysSo()
    {
        var services = new ServiceCollection();
        var slots = new ProvidersBuilder().Default<ISlot, DefaultSlot>().Use<ISlot, HostSlot>().Apply(services);
        services.AddSingleton<ISlot, RegisteredSlot>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISlot>().ShouldBeOfType<RegisteredSlot>();
        Describe(provider, slots).ShouldBe((typeof(RegisteredSlot).FullName, "services", null));
    }

    [Fact]
    public void AFactoryIsNamedByWhatItReturnsAndOneThatThrowsByNothing()
    {
        var (provider, slots) = Apply(new ProvidersBuilder().Default<ISlot>(_ => new DefaultSlot()));
        Describe(provider, slots).ShouldBe((typeof(DefaultSlot).FullName, "default", null));

        (provider, slots) = Apply(new ProvidersBuilder().Default<ISlot>(_ => throw new InvalidOperationException("broken")));
        Describe(provider, slots).ShouldBe((null, "default", null));
    }

    [Fact]
    public void ASlotListsInTheOrderOfItsDefaults()
    {
        var (provider, slots) = Apply(new ProvidersBuilder().Default<ISlot, DefaultSlot>().Default<IOther, Other>());

        slots.Describe(provider).Select(slot => slot.Slot).ShouldBe([typeof(ISlot).FullName, typeof(IOther).FullName]);
    }

    private static (ServiceProvider Provider, ProviderSlotRegistry Slots) Apply(ProvidersBuilder providers, Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        before?.Invoke(services);
        var slots = providers.Apply(services);
        return (services.BuildServiceProvider(), slots);
    }

    private static (string? Implementation, string Source, string? Plugin) Describe(IServiceProvider provider, ProviderSlotRegistry slots)
    {
        var slot = slots.Describe(provider)[0];
        return (slot.Implementation, slot.Source, slot.Plugin);
    }

    public interface ISlot;

    public interface IOther;

    public sealed class DefaultSlot : ISlot;

    public sealed class RegisteredSlot : ISlot;

    public sealed class HostSlot : ISlot;

    public sealed class PluginSlot : ISlot;

    public sealed class OtherPluginSlot : ISlot;

    public sealed class Other : IOther;
}
