using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Plugins;
using FluentCMS.Providers;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Storage;

// A migration of one stream: "system", a plugin's name, or "content".
public sealed record StreamMigration(string Stream, string Id);

// The migrations of one stream that its history holds, and those of its set that it does not.
public sealed record StreamMigrations(string Stream, IReadOnlyList<string> Applied, IReadOnlyList<string> Pending);

public sealed record SchemaSyncReport(IReadOnlyList<string> Applied, IReadOnlyList<string> NotApplied);

// Applies the streams in the order system, plugins, content. MigrationStartupService calls it at startup, and the
// commands `fluentcms migrate` and `migrations list` and `script` call it from the host's command entry point.
public sealed class MigrationRunner(
    IServiceScopeFactory scopes,
    IDataProvider provider,
    SchemaModel schema,
    IEnumerable<IPluginMigrations> plugins,
    IDistributedLock locks,
    ILogger<MigrationRunner> logger,
    IOptions<DatabaseOptions>? databaseOptions = null,
    IHostEnvironment? environment = null,
    TimeProvider? time = null)
{
    public const string SystemStream = "system";
    public const string ContentStream = "content";

    private const string ApplyPending = $"run 'fluentcms migrate' to apply the pending migrations, or set {DatabaseOptions.MigrateOnStartupKey} to true";

    internal const string SchemaSyncLock = "fluentcms:schema-sync";

    private static readonly Stream System = new(SystemStream, services => services.GetRequiredService<SystemDbContext>());
    private static readonly Stream Content = new(ContentStream, services => services.GetRequiredService<CmsDbContext>());

    private readonly SchemaHistory _history = new(scopes, time ?? TimeProvider.System, logger);

    private IEnumerable<Stream> Plugins => plugins.Select(plugin => new Stream(plugin.Name, plugin.ResolveContext));

    private IEnumerable<Stream> SystemAndPlugins => [System, .. Plugins];

    private IEnumerable<Stream> AllStreams => [System, .. Plugins, Content];

    // In the order they apply.
    public IReadOnlyList<string> Streams => [.. AllStreams.Select(stream => stream.Name)];

    public async Task RunAtStartupAsync(CancellationToken cancellationToken = default)
    {
        var options = ResolveOptions();

        // Schema sync leaves the content stream out, so a host may sync on SQLite with content migrations of its server provider.
        var sync = options.MigrateOnStartup && options.SchemaSync == SchemaSyncMode.Auto;
        await RefuseForeignMigrationsAsync(sync ? Plugins : [.. Plugins, Content], cancellationToken);

        if (options.MigrateOnStartup)
        {
            await ApplyAsync(SystemAndPlugins, cancellationToken);

            if (sync)
            {
                var report = await SyncAsync(schema, cancellationToken);
                if (report.NotApplied.Count > 0)
                {
                    await _history.CheckDriftAsync(schema, cancellationToken);
                }

                return;
            }

            await ApplyAsync([Content], cancellationToken);
            await ValidateContentSchemaAsync(cancellationToken);
            await _history.RecordAsync(schema, cancellationToken);
            return;
        }

        await ValidateSystemAndPluginStreamsAsync(cancellationToken);
        await ValidateContentSchemaAsync(cancellationToken);
        await _history.CheckDriftAsync(schema, cancellationToken);
    }

    // Whatever FluentCms:Database:MigrateOnStartup says.
    public async Task<IReadOnlyList<StreamMigration>> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await RefuseForeignMigrationsAsync(cancellationToken);
        var applied = await ApplyAsync([.. SystemAndPlugins, Content], cancellationToken);

        // A database that lacks a column of the schema has not been brought up to it, so the history keeps its old row.
        await using var scope = scopes.CreateAsyncScope();
        var diff = await ContentSchemaDiff.CompareAsync(scope.ServiceProvider.GetRequiredService<CmsDbContext>(), provider.SchemaReader, PluginTablePrefixes(scope.ServiceProvider), cancellationToken);
        if (diff.Missing.Count == 0)
        {
            await _history.RecordAsync(schema, cancellationToken);
        }

        return applied;
    }

    public Task<IReadOnlyList<StreamMigration>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default) =>
        GetPendingAsync(AllStreams, cancellationToken);

    public async Task<IReadOnlyList<StreamMigrations>> ListAsync(CancellationToken cancellationToken = default)
    {
        var streams = new List<StreamMigrations>();
        foreach (var stream in AllStreams)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = stream.ResolveContext(scope.ServiceProvider).Database;
            var applied = await database.GetAppliedMigrationsAsync(cancellationToken);
            var pending = await database.GetPendingMigrationsAsync(cancellationToken);
            streams.Add(new StreamMigrations(stream.Name, [.. applied], [.. pending]));
        }

        return streams;
    }

    // The migrations of a stream's set, oldest first, read from its assembly without opening the database.
    public IReadOnlyList<string> MigrationsOf(string stream)
    {
        using var scope = scopes.CreateScope();
        return [.. Find(stream).ResolveContext(scope.ServiceProvider).Database.GetMigrations()];
    }

    // The SQL of the host's provider that takes a stream from one migration to another ("0" is before the first), without
    // opening the database. A set scaffolded for another provider is refused as MigrateAsync refuses it.
    public async Task<string> ScriptAsync(string stream, string? fromMigration, string? toMigration, bool idempotent, CancellationToken cancellationToken = default)
    {
        var target = Find(stream);
        if (target.Name != SystemStream)
        {
            await RefuseForeignMigrationsAsync([target], cancellationToken);
        }

        await using var scope = scopes.CreateAsyncScope();
        var context = target.ResolveContext(scope.ServiceProvider);
        return context.GetService<IMigrator>().GenerateScript(fromMigration, toMigration, idempotent ? MigrationsSqlGenerationOptions.Idempotent : MigrationsSqlGenerationOptions.Default);
    }

    internal Task<SchemaStatus> GetSchemaStatusAsync(CancellationToken cancellationToken = default) => _history.StatusAsync(schema, cancellationToken);

    // Applies the additive changes a schema needs and reports the rest. Startup calls this for the host's schema;
    // passing another applies a changed schema without a restart. Refuses where schema sync is not Auto.
    public Task<SchemaSyncReport> SyncContentSchemaAsync(SchemaModel? changedSchema = null, CancellationToken cancellationToken = default)
    {
        if (ResolveOptions().SchemaSync != SchemaSyncMode.Auto)
        {
            throw new InvalidOperationException($"Schema sync needs {DatabaseOptions.SchemaSyncKey} = Auto, on SQLite in Development or with AllowSchemaSyncOutsideDevelopment.");
        }

        return SyncAsync(changedSchema ?? schema, cancellationToken);
    }

    // Fails with FCMS1301 if the database lacks a table or column of the content model.
    public async Task ValidateContentSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var diff = await ContentSchemaDiff.CompareAsync(context, provider.SchemaReader, PluginTablePrefixes(scope.ServiceProvider), cancellationToken);
        if (diff.Missing.Count == 0)
        {
            return;
        }

        // A migration that exists only needs applying, which is what FluentCms:Database:MigrateOnStartup = false leaves to do.
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        var fix = pending.Any() ? ApplyPending : $"run 'fluentcms migrations add {diff.SuggestedMigrationName}'";

        throw new FluentCmsConfigurationException(StorageErrorCodes.MissingContentSchema, $"{string.Join("; ", diff.Missing)}; {fix}");
    }

    private Stream Find(string stream) =>
        AllStreams.FirstOrDefault(candidate => candidate.Name == stream)
        ?? throw new ArgumentOutOfRangeException(nameof(stream), stream, $"The streams are {string.Join(", ", Streams)}.");

    private DatabaseStartup ResolveOptions() => DatabaseStartup.Resolve(provider, databaseOptions?.Value, environment, logger);

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

        throw new FluentCmsConfigurationException(StorageErrorCodes.PendingStreamMigrations, $"{string.Join("; ", streams)}; {ApplyPending}");
    }

    // What MigrateAsync checks first, for every stream that it applies.
    internal Task RefuseForeignMigrationsAsync(CancellationToken cancellationToken = default) =>
        RefuseForeignMigrationsAsync([.. Plugins, Content], cancellationToken);

    // EF would apply a set scaffolded for another provider with that provider's store types, and startup validation compares
    // names only, so a switch of provider would go unnoticed. The system stream comes with the provider package.
    private async Task RefuseForeignMigrationsAsync(IEnumerable<Stream> streams, CancellationToken cancellationToken)
    {
        var foreign = new List<string>();
        foreach (var stream in streams)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            if (MigrationProviders.RefusalOf(stream.ResolveContext(scope.ServiceProvider)) is { } refusal)
            {
                foreign.Add($"stream {stream.Name} {refusal}");
            }
        }

        if (foreign.Count > 0)
        {
            throw new FluentCmsConfigurationException(
                StorageErrorCodes.ForeignMigrations,
                $"{string.Join("; ", foreign)}; this host runs {provider.Name}, and a migration set applies only to the provider it was scaffolded for");
        }
    }

    // Every plugin's tables belong to its own stream: the plugins of the plugin host and the streams registered through the seam.
    private List<string> PluginTablePrefixes(IServiceProvider services) =>
        [.. plugins.Select(plugin => plugin.Name)
            .Concat(services.GetService<PluginRegistry>()?.Plugins.Select(plugin => plugin.Name) ?? [])
            .Distinct(StringComparer.Ordinal)
            .Select(PluginStorage.TablePrefixOf)];

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
            using var recorder = AppliedMigrations.Record(context.GetService<IMigrator>());
            await context.Database.MigrateAsync(cancellationToken);
            applied.AddRange(recorder.Ids.Select(id => new StreamMigration(stream.Name, id)));
        }

        return applied;
    }

    private async Task<SchemaSyncReport> SyncAsync(SchemaModel target, CancellationToken cancellationToken)
    {
        // The host's context for the target schema, so that its model maps the plugins' tables as the host's does.
        await using var scope = scopes.CreateAsyncScope();
        await using var context = new CmsDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<CmsDbContext>>().WithSchema(target));

        var prefixes = PluginTablePrefixes(scope.ServiceProvider);
        var diff = await ContentSchemaDiff.CompareAsync(context, provider.SchemaReader, prefixes, cancellationToken);
        if (diff.Additive.Count > 0)
        {
            // Nodes that start together diff the same database, and the second to create a table would fail on it. The
            // lock waits for the first, and the diff taken under it leaves out what the first applied.
            await using var held = await locks.TryAcquireAsync(SchemaSyncLock, Timeout.InfiniteTimeSpan, cancellationToken)
                ?? throw new InvalidOperationException($"The lock {SchemaSyncLock} was not taken.");
            using var guarded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, held.Lost);

            diff = await ContentSchemaDiff.CompareAsync(context, provider.SchemaReader, prefixes, guarded.Token);
            if (diff.Additive.Count > 0)
            {
                var commands = context.GetService<IMigrationsSqlGenerator>().Generate(diff.Additive, context.GetService<IDesignTimeModel>().Model);
                await context.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(commands, context.GetService<IRelationalConnection>(), guarded.Token);
            }
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

        if (diff.NotApplied.Count == 0)
        {
            await _history.RecordAsync(target, cancellationToken);
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
