using FluentCMS.Core.DependencyInjection;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Storage;

// A migration of one stream: "system", a plugin's name, or "content".
public sealed record StreamMigration(string Stream, string Id);

public sealed record SchemaSyncReport(IReadOnlyList<string> Applied, IReadOnlyList<string> NotApplied);

// Applies the streams in the order system, plugins, content. MigrationStartupService calls it at startup, and
// `fluentcms migrate` and `migrate --check` of M6 call MigrateAsync and GetPendingMigrationsAsync.
public sealed class MigrationRunner(
    IServiceScopeFactory scopes,
    IDataProvider provider,
    SchemaModel schema,
    IEnumerable<IPluginMigrations> plugins,
    ILogger<MigrationRunner> logger,
    IConfiguration? configuration = null,
    IHostEnvironment? environment = null)
{
    public const string SystemStream = "system";
    public const string ContentStream = "content";

    private static readonly Stream System = new(SystemStream, services => services.GetRequiredService<SystemDbContext>());
    private static readonly Stream Content = new(ContentStream, services => services.GetRequiredService<CmsDbContext>());

    private IEnumerable<Stream> SystemAndPlugins =>
        [System, .. plugins.Select(plugin => new Stream(plugin.Name, plugin.ResolveContext))];

    public async Task RunAtStartupAsync(CancellationToken cancellationToken = default)
    {
        var options = ResolveOptions();

        if (options.MigrateOnStartup)
        {
            await ApplyAsync(SystemAndPlugins, cancellationToken);

            if (options.SchemaSync == SchemaSyncMode.Auto)
            {
                await SyncAsync(schema, cancellationToken);
                return;
            }

            await ApplyAsync([Content], cancellationToken);
        }
        else
        {
            await ValidateSystemAndPluginStreamsAsync(cancellationToken);
        }

        await ValidateContentSchemaAsync(cancellationToken);
    }

    // Whatever Database:MigrateOnStartup says.
    public Task<IReadOnlyList<StreamMigration>> MigrateAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync([.. SystemAndPlugins, Content], cancellationToken);

    public Task<IReadOnlyList<StreamMigration>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default) =>
        GetPendingAsync([.. SystemAndPlugins, Content], cancellationToken);

    // Applies the additive changes a schema needs and reports the rest. Startup calls this for the host's schema;
    // passing another applies a changed schema without a restart. Refuses outside Development on SQLite.
    public Task<SchemaSyncReport> SyncContentSchemaAsync(SchemaModel? changedSchema = null, CancellationToken cancellationToken = default)
    {
        if (ResolveOptions().SchemaSync != SchemaSyncMode.Auto)
        {
            throw new InvalidOperationException($"Schema sync needs {DatabaseOptions.SchemaSyncKey} = Auto, in Development on SQLite.");
        }

        return SyncAsync(changedSchema ?? schema, cancellationToken);
    }

    // Fails with FCMS1301 if the database lacks a table or column of the content model.
    public async Task ValidateContentSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var diff = await ContentSchemaDiff.CompareAsync(context, provider.SchemaReader, cancellationToken);
        if (diff.Missing.Count == 0)
        {
            return;
        }

        // A migration that exists only needs applying, which is what Database:MigrateOnStartup = false leaves to do.
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        var fix = pending.Any() ? "fluentcms migrate" : $"fluentcms migrations add {diff.SuggestedMigrationName}";

        throw new FluentCmsConfigurationException(StorageErrorCodes.MissingContentSchema, $"{string.Join("; ", diff.Missing)}; run '{fix}'");
    }

    private DatabaseOptions ResolveOptions() => DatabaseOptions.Resolve(provider, configuration, environment, logger);

    // An upgrade can bring a system migration with no content change, and the first query would then fail on a raw
    // missing relation. Content has its own check, which also looks at the database.
    private async Task ValidateSystemAndPluginStreamsAsync(CancellationToken cancellationToken)
    {
        var pending = await GetPendingAsync(SystemAndPlugins, cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var streams = pending
            .GroupBy(migration => migration.Stream)
            .Select(stream => $"stream {stream.Key} has not applied {string.Join(", ", stream.Select(migration => migration.Id))}");

        throw new FluentCmsConfigurationException(StorageErrorCodes.PendingStreamMigrations, $"{string.Join("; ", streams)}; run 'fluentcms migrate'");
    }

    private async Task<IReadOnlyList<StreamMigration>> GetPendingAsync(IEnumerable<Stream> streams, CancellationToken cancellationToken)
    {
        var pending = new List<StreamMigration>();
        foreach (var stream in streams)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = stream.ResolveContext(scope.ServiceProvider);
            pending.AddRange((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Select(id => new StreamMigration(stream.Name, id)));
        }

        return pending;
    }

    private async Task<IReadOnlyList<StreamMigration>> ApplyAsync(IEnumerable<Stream> streams, CancellationToken cancellationToken)
    {
        var applied = new List<StreamMigration>();
        foreach (var stream in streams)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = stream.ResolveContext(scope.ServiceProvider);

            // Migrating with nothing to do would still create the history table.
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                continue;
            }

            MigrationLog.ApplyingStream(logger, pending.Count, stream.Name);
            await context.Database.MigrateAsync(cancellationToken);
            applied.AddRange(pending.Select(id => new StreamMigration(stream.Name, id)));
        }

        return applied;
    }

    private async Task<SchemaSyncReport> SyncAsync(SchemaModel target, CancellationToken cancellationToken)
    {
        await using var context = ContentMigrations.CreateContext(provider, target, typeof(CmsDbContext).Assembly);

        var diff = await ContentSchemaDiff.CompareAsync(context, provider.SchemaReader, cancellationToken);
        if (diff.Additive.Count > 0)
        {
            var commands = context.GetService<IMigrationsSqlGenerator>().Generate(diff.Additive, context.GetService<IDesignTimeModel>().Model);
            await context.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(commands, context.GetService<IRelationalConnection>(), cancellationToken);
        }

        var applied = diff.Additive.Select(Describe).ToList();
        foreach (var change in applied)
        {
            MigrationLog.SchemaSyncApplied(logger, change);
        }

        foreach (var change in diff.NotApplied)
        {
            MigrationLog.SchemaSyncNotApplied(logger, change);
        }

        return new SchemaSyncReport(applied, diff.NotApplied);
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        CreateTableOperation table => $"create table {table.Name}",
        AddColumnOperation column => $"add column {column.Name} to table {column.Table}",
        CreateIndexOperation index => $"create index {index.Name} on table {index.Table}",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation.GetType().Name, null),
    };

    private sealed record Stream(string Name, Func<IServiceProvider, DbContext> ResolveContext);
}
