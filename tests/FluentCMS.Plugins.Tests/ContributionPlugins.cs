using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Plugins.Sample;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FluentCMS.Plugins.Tests;

// A second plugin with a stream, after the sample it depends on. Its model is empty, so its one hand-written migration
// needs no snapshot.
[FluentCmsPlugin("late", SdkRange = "[1.0.0,2.0.0)", DependsOn = ["sample"])]
public sealed class LatePlugin : IPlugin
{
    public const string Table = "cms_late_item";
    public const string MigrationId = "20261106000000_InitialLate";

    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddMigrations(typeof(LatePlugin).Assembly);

    public void Configure(IPluginAppBuilder app)
    {
    }
}

[DbContext(typeof(PluginDbContext<LatePlugin>))]
[Migration(LatePlugin.MigrationId)]
public sealed class InitialLate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: LatePlugin.Table,
            columns: table => new { id = table.Column<Guid>(nullable: false) },
            constraints: table => table.PrimaryKey("PK_cms_late_item", item => item.id));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(LatePlugin.Table);
}

[FluentCmsPlugin("stray", SdkRange = "[1.0.0,2.0.0)")]
public sealed class StrayTablePlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddModel<StrayModel>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class StrayModel : IPluginModelContributor
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<SampleNote>().ToTable("cms_other_item");
}

[FluentCmsPlugin("history", SdkRange = "[1.0.0,2.0.0)")]
public sealed class HistoryTablePlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddModel<HistoryModel>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class HistoryModel : IPluginModelContributor
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<SampleNote>().ToTable("cms_history__migrations");
}

[FluentCmsPlugin("twice", SdkRange = "[1.0.0,2.0.0)")]
public sealed class TwiceMigratedPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin
        .AddMigrations(typeof(TwiceMigratedPlugin).Assembly)
        .AddMigrations(typeof(SamplePlugin).Assembly);

    public void Configure(IPluginAppBuilder app)
    {
    }
}

[FluentCmsPlugin("coded", SdkRange = "[1.0.0,2.0.0)")]
public sealed class CodedPlugin : IPlugin
{
    public string Code { get; set; } = "FCMS9902";

    public int Status { get; set; } = 409;

    public string Title { get; set; } = "Coded";

    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddErrorCode(Code, Status, Title);

    public void Configure(IPluginAppBuilder app)
    {
    }
}

[FluentCmsPlugin("typed", SdkRange = "[1.0.0,2.0.0)")]
public sealed class TextImpostorPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddFieldType<TextImpostorFieldType>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

[FluentCmsPlugin("excerpt", SdkRange = "[1.0.0,2.0.0)")]
public sealed class ExcerptImpostorPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddFieldType<ExcerptImpostorFieldType>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public abstract class ImpostorFieldType(string name) : IFieldType
{
    public string Name => name;

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(string)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator>();

    public virtual IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [];

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => false;

    public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        result = value;
        message = null;
        return true;
    }

    public JsonObject OpenApiSchema(SchemaField field) => new() { ["type"] = "string" };
}

public sealed class TextImpostorFieldType() : ImpostorFieldType("text");

public sealed class ExcerptImpostorFieldType() : ImpostorFieldType(ExcerptFieldType.TypeName);

[FluentCmsPlugin("routed", SdkRange = "[1.0.0,2.0.0)")]
public sealed class RoutedPlugin : IPlugin
{
    public string Prefix { get; set; } = "/routed";

    public bool Management { get; set; }

    public bool Delivery { get; set; }

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
    }

    public void Configure(IPluginAppBuilder app)
    {
        var group = Management ? app.MapManagement(Prefix) : Delivery ? app.MapDelivery(Prefix) : app.MapGroup(Prefix);
        group.MapGet("/ping", () => "pong");

        // Each way an endpoint ends up a plain RequestDelegate: cast, bound by overload resolution, and given to Map.
        group.MapGet("/raw", (RequestDelegate)(context => context.Response.WriteAsync("raw")));
        group.MapGet("/context", (HttpContext context) => context.Response.WriteAsync("context"));
        group.Map("/mapped", context => context.Response.WriteAsync("mapped"));
        group.MapGet("/open", (HttpContext context) => context.Response.WriteAsync("open")).AllowAnonymous();
    }
}

public sealed class FakeServiceBuilder : IPluginServiceBuilder
{
    public string Name => "fake";

    public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; } = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

    public FluentCMS.Providers.IProviderSlots Providers => throw new NotSupportedException();

    public IPluginServiceBuilder AddFieldType<TFieldType>()
        where TFieldType : class, IFieldType, new() => this;

    public IPluginServiceBuilder AddPermissions<TProvider>()
        where TProvider : class, FluentCMS.Authorization.IPermissionProvider => this;

    public IPluginServiceBuilder AddSettings<TSettings>()
        where TSettings : class, new() => this;

    public IPluginServiceBuilder AddProviderSlot<TSlot, TDefault>()
        where TSlot : class
        where TDefault : class, TSlot => this;

    public IPluginServiceBuilder AddJob<TJob, TPayload>(string type, TimeSpan? attemptTimeout = null)
        where TJob : class, FluentCMS.Jobs.IJob<TPayload> => this;

    public IPluginServiceBuilder AddJobQueue(string name, Func<IServiceProvider, int> workers) => this;

    public IPluginServiceBuilder AddEventHandler<TEvent, THandler>()
        where TEvent : FluentCMS.Events.CmsEvent
        where THandler : class, FluentCMS.Events.IEventHandler<TEvent> => this;

    public IPluginServiceBuilder AddOutboxHandler<TEvent, THandler>(string name)
        where TEvent : FluentCMS.Events.CmsEvent
        where THandler : class, FluentCMS.Events.IOutboxEventHandler<TEvent> => this;

    public IPluginServiceBuilder AddActivity<TEvent>(string name, Func<TEvent, FluentCMS.Activity.ActivityEntry> describe)
        where TEvent : FluentCMS.Events.CmsEvent => this;

    public IPluginServiceBuilder AddErrorCode(string code, int status, string title) => this;
}


// A plugin's prefix can hold another plugin's tables or a system table, which the prefix check alone would let through.
[FluentCmsPlugin("ai", SdkRange = "[1.0.0,2.0.0)")]
public sealed class AiPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddModel<AssistUsageModel>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

[FluentCmsPlugin("ai-assist", SdkRange = "[1.0.0,2.0.0)")]
public sealed class AiAssistPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddModel<AssistUsageModel>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class AssistUsageModel : IPluginModelContributor
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<SampleNote>().ToTable("cms_ai_assist_usage");
}

[FluentCmsPlugin("site", SdkRange = "[1.0.0,2.0.0)")]
public sealed class SiteTablePlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddModel<SiteLocaleModel>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class SiteLocaleModel : IPluginModelContributor
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<SampleNote>().ToTable("cms_site_locale");
}

[FluentCmsPlugin("required", SdkRange = "[1.0.0,2.0.0)")]
public sealed class RequiredReferencePlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddFieldType<RequiredReferenceFieldType>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class RequiredReferenceFieldType() : ImpostorFieldType("requiredExcerpt")
{
    public override IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [new FieldSettingDescriptor { Key = "of", Kind = FieldSettingKind.FieldReference, Required = true }];
}
