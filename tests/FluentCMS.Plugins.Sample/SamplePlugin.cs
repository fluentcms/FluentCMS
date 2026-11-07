using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Hooks;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Sample;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsPlugin(typeof(SamplePlugin))]

namespace FluentCMS.Plugins.Sample;

// Discovered by every host of the plugin tests, with a contribution of each kind. It records its calls only when the test
// registered a PluginCalls.
[FluentCmsPlugin("sample", Version = "1.2.0", SdkRange = "[1.0.0,2.0.0)")]
public sealed class SamplePlugin : IPlugin
{
    public const string NoteExists = "FCMS9901";

    public string Greeting { get; set; } = "discovered";

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        plugin.Services.AddSingleton(new SampleGreeting(Greeting));
        plugin
            .AddFieldType<ExcerptFieldType>()
            .AddPermissions<SamplePermissions>()
            .AddSettings<SampleSettings>()
            .AddModel<SampleModel>()
            .AddMigrations(typeof(SamplePlugin).Assembly)
            .AddJob<SampleNoteJob, SampleNotePayload>(SampleNoteJob.Type)
            .AddOutboxHandler<SampleNoteAdded, SampleNoteHandler>("sample.note-added")
            .AddActivity<SampleNoteAdded>("sample.note_added", added => new ActivityEntry("sample.note_added")
            {
                TargetType = "sample-note",
                TargetId = added.NoteId.ToString(),
                TargetLabel = added.Text,
            })
            .AddErrorCode(NoteExists, 409, "Note exists");

        if (PluginCalls.In(plugin.Services) is { } calls)
        {
            calls.Add($"{plugin.Name}.services");
            plugin.Services.AddScoped<IBeforeCreateHook<ContentDocument>>(_ => new SampleHook(calls));
        }
    }

    public void Configure(IPluginAppBuilder app)
    {
        app.Services.GetRequiredService<SampleGreeting>().Configured = true;
        app.Services.GetService<PluginCalls>()?.Add($"{app.Name}.configure");

        app.MapGroup("/sample").MapSampleEndpoints();
        app.MapManagement("/sample").MapSampleManagementEndpoints();
    }
}

public sealed class SampleGreeting(string text)
{
    public string Text => text;

    public bool Configured { get; set; }
}

[HookOrder(5)]
public sealed class SampleHook(PluginCalls calls) : IBeforeCreateHook<ContentDocument>
{
    public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
    {
        calls.Add("sample.hook");
        return ValueTask.CompletedTask;
    }
}
