using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Plugins;
using FluentCMS.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Plugins;

public class PluginHostTests
{
    private static readonly SemanticVersion Sdk = SemanticVersion.Parse("1.0.0")!;

    [Theory]
    [InlineData(typeof(AbstractPlugin))]
    [InlineData(typeof(NotAPlugin))]
    [InlineData(typeof(NoDefaultConstructorPlugin))]
    [InlineData(typeof(TypeFormPlugin))]
    public void ADiscoveredTypeThatCannotBeAPluginStopsTheHost(Type type)
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => PluginHost.Compose([], [type], Sdk));

        exception.Code.ShouldBe("FCMS1501");
        exception.Message.ShouldContain(type.Name);
    }

    [Fact]
    public void ADiscoveredPluginsOwnExceptionStopsTheHostUnwrapped()
    {
        var exception = Should.Throw<InvalidOperationException>(() => PluginHost.Compose([], [typeof(ThrowingPlugin)], Sdk));

        exception.Message.ShouldBe("not today");
    }

    [Fact]
    public void ADiscoveredPluginIsCreatedAndKeepsItsPlace()
    {
        var plugins = PluginHost.Compose([new FirstPlugin()], [typeof(SecondPlugin), typeof(FirstPlugin)], Sdk);

        plugins.Select(plugin => (plugin.Name, plugin.Source)).ShouldBe([("first", PluginSource.Explicit), ("second", PluginSource.Discovered)]);
        plugins[1].Instance.ShouldBeOfType<SecondPlugin>();
    }

    // This test assembly is the app's entry assembly, so its other types are the host's even with one plugin in it.
    [Fact]
    public void AHostTypeInTheAppsAssemblyIsNotRankedAsItsOnlyPlugin()
    {
        var registry = PluginHost.ConfigureServices(new FluentCmsBuilder(new ServiceCollection()), PluginHost.Compose([new FirstPlugin()], [], Sdk));

        registry.Rank(typeof(FirstPlugin)).ShouldBe(-1);
        registry.Rank(typeof(SecondPlugin)).ShouldBe(0);
    }

    [Fact]
    public void ConfigureStartsOncePerHost()
    {
        var registry = PluginHost.ConfigureServices(new FluentCmsBuilder(new ServiceCollection()), []);

        registry.TryStartConfigure().ShouldBeTrue();
        registry.TryStartConfigure().ShouldBeFalse();
    }

    public abstract class Recorded : IPlugin
    {
        public void ConfigureServices(IPluginServiceBuilder plugin)
        {
        }

        public void Configure(IPluginAppBuilder app)
        {
        }
    }

    [FluentCmsPlugin("abstract", SdkRange = "1.0")]
    public abstract class AbstractPlugin : Recorded;

    [FluentCmsPlugin("not-a-plugin", SdkRange = "1.0")]
    public sealed class NotAPlugin;

    [FluentCmsPlugin("no-default", SdkRange = "1.0")]
    public sealed class NoDefaultConstructorPlugin(string name) : Recorded
    {
        public string Name => name;
    }

    [FluentCmsPlugin(typeof(FirstPlugin))]
    public sealed class TypeFormPlugin : Recorded;

    [FluentCmsPlugin("throwing", SdkRange = "1.0")]
    public sealed class ThrowingPlugin : Recorded
    {
        public ThrowingPlugin() => throw new InvalidOperationException("not today");
    }

    [FluentCmsPlugin("first", SdkRange = "1.0")]
    public sealed class FirstPlugin : Recorded;

    [FluentCmsPlugin("second", SdkRange = "1.0")]
    public sealed class SecondPlugin : Recorded;
}
