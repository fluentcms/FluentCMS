using FluentCMS.Core.DependencyInjection;
using FluentCMS.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

public sealed class PluginValidationTests
{
    [Fact]
    public void AnSdkRangeTheRunningSdkDoesNotSatisfyStopsTheHost() =>
        Refused(cms => cms.AddPlugin<FuturePlugin>(), "FCMS1505", "'future'", "[99.0.0,)");

    [Fact]
    public void AMissingDependencyStopsTheHost() =>
        Refused(cms => cms.AddPlugin<LonelyPlugin>(), "FCMS1507", "'lonely'", "'missing'");

    [Fact]
    public void ADependencyCycleStopsTheHost() =>
        Refused(cms => cms.AddPlugin<ChickenPlugin>().AddPlugin<EggPlugin>(), "FCMS1508", "'chicken' -> 'egg' -> 'chicken'");

    [Fact]
    public void TwoPluginsWithOneNameStopTheHost()
    {
        Refused(cms => cms.AddPlugin<TwinPlugin>().AddPlugin<OtherTwinPlugin>(), "FCMS1506", "'twin'", nameof(TwinPlugin), nameof(OtherTwinPlugin));
        Refused(cms => cms.AddPlugin<ImpostorPlugin>(), "FCMS1506", "'sample'", nameof(ImpostorPlugin), "SamplePlugin");
        Refused(cms => cms.AddPlugin<TwinPlugin>().AddPlugin<TwinPlugin>(), "FCMS1506", "'twin'", "more than once");
    }

    [Fact]
    public void ABadManifestStopsTheHost()
    {
        Refused(cms => cms.AddPlugin<BadNamePlugin>(), "FCMS1502", "'Bad_Name'");
        Refused(cms => cms.AddPlugin<BadVersionPlugin>(), "FCMS1503", "'versioned'", "'one'");
        Refused(cms => cms.AddPlugin<NoRangePlugin>(), "FCMS1504", "'unranged'", "no SdkRange");
        Refused(cms => cms.AddPlugin<BadRangePlugin>(), "FCMS1504", "'misranged'", "[2.0.0,1.0.0]");
        Refused(cms => cms.AddPlugin<NoManifestPlugin>(), "FCMS1501", nameof(NoManifestPlugin));
    }

    private static void Refused(Action<FluentCmsBuilder> configure, string code, params string[] named)
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => new ServiceCollection().AddFluentCms(cms =>
        {
            cms.UseSqlite("Data Source=:memory:");
            configure(cms);
        }));

        exception.Code.ShouldBe(code);
        foreach (var name in named)
        {
            exception.Message.ShouldContain(name);
        }
    }
}
