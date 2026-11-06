using FluentCMS.Content;
using FluentCMS.Hooks;
using FluentCMS.Plugins.Sample;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Tests;

// Plugins that the tests add explicitly: this assembly names none, so none of them is discovered.
public abstract class RecordingPlugin : IPlugin
{
    public virtual void ConfigureServices(IPluginServiceBuilder plugin) => PluginCalls.In(plugin.Services)?.Add($"{plugin.Name}.services");

    public void Configure(IPluginAppBuilder app) => app.Services.GetService<PluginCalls>()?.Add($"{app.Name}.configure");
}

[FluentCmsPlugin("alpha", SdkRange = "[1.0.0,2.0.0)")]
public sealed class AlphaPlugin : RecordingPlugin;

[FluentCmsPlugin("beta", Version = "2.0.0", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["gamma"])]
public sealed class BetaPlugin : RecordingPlugin;

[FluentCmsPlugin("gamma", Version = "3.0.0", SdkRange = "1.0")]
public sealed class GammaPlugin : RecordingPlugin;

[FluentCmsPlugin("needs-sample", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["sample"])]
public sealed class NeedsSamplePlugin : RecordingPlugin;

[FluentCmsPlugin("stamp", SdkRange = "[1.0.0,2.0.0)")]
public sealed class StampPlugin : RecordingPlugin
{
    public override void ConfigureServices(IPluginServiceBuilder plugin)
    {
        base.ConfigureServices(plugin);
        plugin.Services.AddScoped<IBeforeCreateHook<Note>, StampHook>();
    }
}

[FluentCmsPlugin("audit", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["stamp"])]
public sealed class AuditPlugin : RecordingPlugin
{
    public override void ConfigureServices(IPluginServiceBuilder plugin)
    {
        base.ConfigureServices(plugin);
        plugin.Services.AddScoped<IBeforeCreateHook<ContentDocument>, AuditHook>();
    }
}

[HookOrder(5)]
public sealed class StampHook(PluginCalls calls) : IBeforeCreateHook<Note>
{
    public ValueTask BeforeCreateAsync(HookContext<Note> context, CancellationToken cancellationToken)
    {
        calls.Add("stamp.hook");
        return ValueTask.CompletedTask;
    }
}

[HookOrder(5)]
public sealed class AuditHook(PluginCalls calls) : IBeforeCreateHook<ContentDocument>
{
    public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
    {
        calls.Add("audit.hook");
        return ValueTask.CompletedTask;
    }
}

[HookOrder(5)]
public sealed class HostHook(PluginCalls calls) : IBeforeCreateHook<ContentDocument>
{
    public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
    {
        calls.Add("host.hook");
        return ValueTask.CompletedTask;
    }
}

[Collection("note")]
public sealed class Note : ContentItem
{
    [Text(MaxLength = 100), Required]
    public string Title { get; set; } = "";
}

[FluentCmsPlugin("future", SdkRange = "[99.0.0,)")]
public sealed class FuturePlugin : RecordingPlugin;

[FluentCmsPlugin("lonely", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["missing"])]
public sealed class LonelyPlugin : RecordingPlugin;

[FluentCmsPlugin("chicken", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["egg"])]
public sealed class ChickenPlugin : RecordingPlugin;

[FluentCmsPlugin("egg", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["chicken"])]
public sealed class EggPlugin : RecordingPlugin;

[FluentCmsPlugin("twin", SdkRange = "[1.0.0,2.0.0)")]
public sealed class TwinPlugin : RecordingPlugin;

[FluentCmsPlugin("twin", SdkRange = "[1.0.0,2.0.0)")]
public sealed class OtherTwinPlugin : RecordingPlugin;

[FluentCmsPlugin("sample", SdkRange = "[1.0.0,2.0.0)")]
public sealed class ImpostorPlugin : RecordingPlugin;

[FluentCmsPlugin("Bad_Name", SdkRange = "[1.0.0,2.0.0)")]
public sealed class BadNamePlugin : RecordingPlugin;

[FluentCmsPlugin("versioned", Version = "one", SdkRange = "[1.0.0,2.0.0)")]
public sealed class BadVersionPlugin : RecordingPlugin;

[FluentCmsPlugin("unranged")]
public sealed class NoRangePlugin : RecordingPlugin;

[FluentCmsPlugin("misranged", SdkRange = "[2.0.0,1.0.0]")]
public sealed class BadRangePlugin : RecordingPlugin;

public sealed class NoManifestPlugin : RecordingPlugin;
