using System.Reflection;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins;
using FluentCMS.Plugins.AiAssist;
using FluentCMS.Plugins.Mcp;
using FluentCMS.Plugins.Media;
using FluentCMS.Plugins.MultiSite;
using FluentCMS.Plugins.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// The migration stream of every first-party plugin, applied on the provider of the run. The plugins' own tests check their
// sets against the model without a database, so this is where the PostgreSQL and SQL Server sets meet a server. The
// project references the plugins and turns their discovery off (FluentCMS.Plugins.DisableDiscovery in the project file),
// so that only the hosts of this class load them.
public sealed class PluginStreamTests : IDisposable
{
    // A plugin that gets a migration stream adds its line here.
    private static readonly PluginStream[] Streams =
    [
        PluginStream.Of<MediaPlugin>(),
        PluginStream.Of<WebhooksPlugin>(),
        PluginStream.Of<McpPlugin>(),
        PluginStream.Of<MultiSitePlugin>(),
        PluginStream.Of<AiAssistPlugin>(),
    ];

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task TheStreamOfEveryFirstPartyPluginAppliesOnTheProviderOfTheRun()
    {
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            "Production",
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            configure: cms => Array.ForEach(Streams, stream => stream.Add(cms)));
        var runner = host.Services.GetRequiredService<MigrationRunner>();

        var pending = await runner.GetPendingMigrationsAsync(Token);
        var applied = await runner.MigrateAsync(Token);

        pending.Select(migration => migration.Stream).Distinct().ShouldBe([MigrationRunner.SystemStream, .. Streams.Select(stream => stream.Name), MigrationRunner.ContentStream]);
        applied.ShouldBe(pending);
        (await runner.GetPendingMigrationsAsync(Token)).ShouldBeEmpty();

        using var scope = host.Services.CreateScope();
        foreach (var stream in Streams)
        {
            var ids = applied.Where(migration => migration.Stream == stream.Name).Select(migration => migration.Id).ToList();
            ids.ShouldNotBeEmpty(stream.Name);
            _database.ReadStrings($"select \"MigrationId\" from {stream.HistoryTable}").ShouldBe(ids, ignoreOrder: true, stream.Name);
            ReadEveryTable(stream.Context(scope.ServiceProvider));
        }
    }

    // Streams is kept by hand, so a plugin that gets migrations and no line there would never meet a server.
    [Fact]
    public void EveryFirstPartyPluginWithMigrationsIsInTheList()
    {
        var plugins = Path.Combine(RepositoryRoot(), "src", "plugins");
        var withMigrations = Directory.EnumerateDirectories(plugins)
            .Where(project => Directory.Exists(Path.Combine(project, "Migrations")))
            .Select(Path.GetFileName)
            .ToList();

        withMigrations.ShouldNotBeEmpty();
        withMigrations.ShouldBe(Streams.Select(stream => stream.Project), ignoreOrder: true);
    }

    // The tests of this project start hosts of their own, which must not pick up the plugins the project references.
    [Fact]
    public void AHostOfTheseTestsLoadsNoPluginItDoesNotAdd()
    {
        using var services = _database.CreateServices(ArticleSchemas.Titled);

        PluginDiscovery.Types.ShouldBeEmpty();
        services.GetServices<IPluginMigrations>().ShouldBeEmpty();
    }

    // A referenced plugin that does not run leaves a line in the log that says why.
    [Fact]
    public async Task AHostWithDiscoveryOffSaysSoWhenItStarts()
    {
        var logs = new RecordingLogs();
        using var host = _database.CreateHost(
            ArticleSchemas.Titled,
            "Production",
            GeneratedMigrations.CompileFirst(ArticleSchemas.Titled),
            services: services => services.AddSingleton<ILoggerProvider>(logs));

        await host.StartAsync(Token);
        await host.StopAsync(Token);

        logs.Count("DiscoveryDisabled").ShouldBe(1);
    }

    // Reads every table of the plugin through its model, so that a table or a column the model has and the applied set
    // lacks, or has with another type, fails on the provider that has it so.
    private static void ReadEveryTable(DbContext context)
    {
        var entities = context.Model.GetEntityTypes().ToList();
        entities.ShouldNotBeEmpty();
        foreach (var entity in entities)
        {
            var rows = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(entity.ClrType).Invoke(context, null)!;
            rows.ToList().ShouldBeEmpty(entity.GetTableName());
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    // Project is the plugin's project under src/plugins, which is named after its assembly.
    private sealed record PluginStream(string Name, string Project, Action<FluentCmsBuilder> Add, Func<IServiceProvider, DbContext> Context)
    {
        public string HistoryTable => $"cms_{Name.Replace('-', '_')}__migrations";

        public static PluginStream Of<TPlugin>()
            where TPlugin : class, IPlugin, new() =>
            new(
                typeof(TPlugin).GetCustomAttribute<FluentCmsPluginAttribute>()!.Name!,
                typeof(TPlugin).Assembly.GetName().Name!,
                cms => cms.AddPlugin<TPlugin>(),
                services => services.GetRequiredService<PluginDbContext<TPlugin>>());
    }
}
