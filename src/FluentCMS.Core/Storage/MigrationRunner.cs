using System.Text;
using FluentCMS.Core.Caching;
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

    // Far above a healthy wait: a schema apply that rebuilds large tables can hold the lock for minutes. It still ends a wait
    // on a lease that never runs out by this instance's clock.
    internal static readonly TimeSpan SchemaSyncWait = TimeSpan.FromMinutes(15);

    // As far above a healthy wait: another process may be applying a migration that rebuilds large tables.
    private static readonly TimeSpan MigrationsLockWait = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan MigrationsLockWaitLogged = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FirstMigrationsLockPoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongestMigrationsLockPoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReportedMigrationsLockPoll = TimeSpan.FromSeconds(5);

    private static readonly Stream System = new(SystemStream, services => services.GetRequiredService<SystemDbContext>());
    private static readonly Stream Content = new(ContentStream, services => services.GetRequiredService<CmsDbContext>());

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private readonly SchemaHistory _history = new(scopes, time ?? TimeProvider.System, logger);

    private readonly IPluginMigrations[] _plugins = Checked(plugins);

    private IEnumerable<Stream> Plugins => _plugins.Select(plugin => new Stream(plugin.Name, plugin.ResolveContext));

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
            await CheckAndApplyAsync(sync ? SystemAndPlugins : AllStreams, cancellationToken);

            if (sync)
            {
                var (_, recorded) = await SyncAsync(schema, cancellationToken);
                if (!recorded)
                {
                    await _history.CheckDriftAsync(schema, UsesSchemaApplyAsync, cancellationToken);
                }

                return;
            }

            await ValidateContentSchemaAsync(cancellationToken);
            if (await HasContentMigrationsAsync(cancellationToken))
            {
                await _history.RecordAsync(schema, cancellationToken);
            }
            else
            {
                await _history.CheckDriftAsync(schema, _ => Task.FromResult(true), cancellationToken);
            }

            return;
        }

        await RefuseMigrationsBeforeBaselineAsync(cancellationToken);
        await RefuseEarlierBaselineAsync(cancellationToken);
        await ValidateSystemAndPluginStreamsAsync(cancellationToken);
        await ValidateContentSchemaAsync(cancellationToken);
        await _history.CheckDriftAsync(schema, UsesSchemaApplyAsync, cancellationToken);
    }

    // Whatever FluentCms:Database:MigrateOnStartup says.
    public async Task<IReadOnlyList<StreamMigration>> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await RefuseForeignMigrationsAsync(cancellationToken);
        var applied = await CheckAndApplyAsync(AllStreams, cancellationToken);
        if (!await HasContentMigrationsAsync(cancellationToken))
        {
            return applied;
        }

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

    internal Task<string?> GetAppliedSnapshotAsync(CancellationToken cancellationToken = default) => _history.AppliedSnapshotAsync(cancellationToken);

    // What schema apply checks first: the content migrations of the host's set and of the content history, which own the
    // content tables of a host that has any, and what the system and plugin streams have not applied.
    internal async Task<IReadOnlyList<string>> GetContentMigrationsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = Content.ResolveContext(scope.ServiceProvider).Database;
        return [.. database.GetMigrations().Union(await database.GetAppliedMigrationsAsync(cancellationToken), StringComparer.Ordinal)];
    }

    // Content migrations bring a host's content tables up to its schema, so migrate and a start that migrates record the
    // schema history. Without them schema apply or schema sync does, and only they record it, so that the newest row keeps
    // telling schema apply what it still has to change.
    private async Task<bool> HasContentMigrationsAsync(CancellationToken cancellationToken) =>
        (await GetContentMigrationsAsync(cancellationToken)).Count > 0;

    private async Task<bool> UsesSchemaApplyAsync(CancellationToken cancellationToken) => !await HasContentMigrationsAsync(cancellationToken);

    internal Task<IReadOnlyList<StreamMigration>> GetPendingSystemAndPluginMigrationsAsync(CancellationToken cancellationToken = default) =>
        GetPendingAsync(SystemAndPlugins, cancellationToken);

    // Held by schema apply and schema sync, which change the same tables, so that two runs apply each operation once.
    internal async Task<IDistributedLockHandle> LockContentSchemaAsync(CancellationToken cancellationToken = default) =>
        await locks.TryAcquireAsync(SchemaSyncLock, SchemaSyncWait, cancellationToken) ?? throw SchemaSyncBusy();

    private static TimeoutException SchemaSyncBusy() => new(
        $"The lock {SchemaSyncLock} was not free within {SchemaSyncWait.TotalMinutes:0} minutes, so the content tables were not changed; " +
        $"check the owner and lease_until of its row in {StorageNames.Lock}: a lease_until that keeps moving means another instance or a " +
        "'fluentcms schema apply' still holds it and is working, and one that stands still ahead of this instance's clock means its holder " +
        "stopped and the clocks of the instances are not in step; on SQLite one that stands still behind the clock can also mean that its " +
        "holder is working inside a long write transaction, which keeps it from renewing until the transaction ends; when the log has " +
        $"event {LockLog.TimedOutBusyId} for the lock, it can also mean that the database was busy: another connection kept SQLite's one " +
        "writer to the end of the wait, and the row may be free; once no instance is running, set the row's owner to null.");

    // The steps that bring the content tables up to the host's schema: schema sync's comparison, with renames and the
    // changes sync leaves undone. The applied snapshot tells which tables under a plugin's prefix are content tables.
    internal async Task<SchemaApplyPlan> PlanContentSchemaAsync(string? appliedSnapshot, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var diff = await ContentSchemaDiff.PlanAsync(context, schema, appliedSnapshot, provider.SchemaReader, PluginTablePrefixes(scope.ServiceProvider), cancellationToken);
        return new SchemaApplyPlan(diff.Steps, diff.LeftAlone);
    }

    // With the SQL of the provider's migrations generator, which EF's executor runs in one transaction; it commits early
    // only before a command the provider runs outside one, such as the foreign key pragma of a SQLite table rebuild.
    internal async Task ApplyContentSchemaAsync(IReadOnlyList<SchemaApplyStep> steps, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(ContentSchemaDiff.OperationsOf(steps), context.GetService<IDesignTimeModel>().Model);
        await context.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(commands, context.GetService<IRelationalConnection>(), cancellationToken);
    }

    // The same SQL as a script in the provider's dialect, in a transaction as `migrations script` writes one.
    internal string ScriptContentSchema(IReadOnlyList<SchemaApplyStep> steps)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var sql = context.GetService<ISqlGenerationHelper>();
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(ContentSchemaDiff.OperationsOf(steps), context.GetService<IDesignTimeModel>().Model, MigrationsSqlGenerationOptions.Script);

        var script = new StringBuilder();
        var inTransaction = false;
        foreach (var command in commands)
        {
            if (!inTransaction && !command.TransactionSuppressed)
            {
                script.AppendLine(sql.StartTransactionStatement).Append(sql.BatchTerminator);
                inTransaction = true;
            }
            else if (inTransaction && command.TransactionSuppressed)
            {
                script.AppendLine(sql.CommitTransactionStatement).Append(sql.BatchTerminator);
                inTransaction = false;
            }

            script.Append(command.CommandText).Append(sql.BatchTerminator);
        }

        if (inTransaction)
        {
            script.AppendLine(sql.CommitTransactionStatement).Append(sql.BatchTerminator);
        }

        return script.ToString();
    }

    // Unlike the runner's own record, a row that cannot be written fails: it is what schema apply was asked for.
    internal Task RecordSchemaAsync(CancellationToken cancellationToken = default) => _history.RecordOrThrowAsync(schema, cancellationToken);

    // Applies the additive changes a schema needs and reports the rest. Startup calls this for the host's schema;
    // passing another applies a changed schema without a restart. Refuses where schema sync is not Auto.
    public async Task<SchemaSyncReport> SyncContentSchemaAsync(SchemaModel? changedSchema = null, CancellationToken cancellationToken = default)
    {
        if (ResolveOptions().SchemaSync != SchemaSyncMode.Auto)
        {
            throw new InvalidOperationException($"Schema sync needs {DatabaseOptions.SchemaSyncKey} = Auto, on SQLite in Development or with AllowSchemaSyncOutsideDevelopment.");
        }

        return (await SyncAsync(changedSchema ?? schema, cancellationToken)).Report;
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
        // A host with no content migrations may have types that are data, whose tables schema apply brings up to the schema.
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        var fix = pending.Any() ? ApplyPending
            : context.Database.GetMigrations().Any() ? $"run 'fluentcms migrations add {diff.SuggestedMigrationName}'"
            : $"run 'fluentcms schema apply', or 'fluentcms migrations add {diff.SuggestedMigrationName}' to give the host content migrations";

        throw new FluentCmsConfigurationException(StorageErrorCodes.MissingContentSchema, $"{string.Join("; ", diff.Missing)}; {fix}");
    }

    // A plugin's name is its stream's, which names its history table and what the commands take, so core's are not one.
    internal static bool IsCoreStream(string name) => name is SystemStream or ContentStream;

    // The plugin host checks the names of its plugins; a stream registered through the seam is checked here.
    private static IPluginMigrations[] Checked(IEnumerable<IPluginMigrations> plugins)
    {
        IPluginMigrations[] streams = [.. plugins];
        if (streams.FirstOrDefault(stream => IsCoreStream(stream.Name)) is { } taken)
        {
            throw new FluentCmsConfigurationException(
                PluginErrorCodes.InvalidName,
                $"The plugin stream {taken.GetType().FullName} is named '{taken.Name}', which is a stream of core; '{SystemStream}' and '{ContentStream}' are not plugin names.");
        }

        return streams;
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

    // What a start checks: without the content stream when schema sync runs, as RunAtStartupAsync does.
    internal Task RefuseForeignMigrationsAtStartupAsync(CancellationToken cancellationToken = default)
    {
        var options = ResolveOptions();
        var sync = options.MigrateOnStartup && options.SchemaSync == SchemaSyncMode.Auto;
        return RefuseForeignMigrationsAsync(sync ? Plugins : [.. Plugins, Content], cancellationToken);
    }

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

    // Each system and plugin stream starts with one baseline migration (#3052). A history that holds migrations but not the
    // baseline was written by the chain from before it, and EF would apply the baseline over tables that exist. Previews
    // do not upgrade a database until 1.0.0, so it is refused before anything is applied.
    internal async Task RefuseMigrationsBeforeBaselineAsync(CancellationToken cancellationToken = default)
    {
        var refused = new List<string>();
        foreach (var stream in SystemAndPlugins)
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = stream.ResolveContext(scope.ServiceProvider).Database;
            if (database.GetMigrations().FirstOrDefault() is not { } baseline)
            {
                continue;
            }

            var applied = (await database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            if (applied.Count > 0 && !applied.Contains(baseline, StringComparer.Ordinal))
            {
                refused.Add($"stream {stream.Name} has applied {applied[0]} but not its baseline {baseline}");
            }
        }

        if (refused.Count > 0)
        {
            throw new FluentCmsConfigurationException(
                StorageErrorCodes.MigrationsBeforeBaseline,
                $"{string.Join("; ", refused)}; nothing was applied: the database was made by the migrations from before the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again");
        }
    }

    // Until 1.0.0 a change of the model goes into each stream's baseline (10.4, H5), so a database made by an earlier build of
    // a baseline holds it in its history with nothing pending and lacks what was added to it since, on which the first query
    // would fail with a raw missing relation. A stream whose history holds its baseline and that has nothing pending must
    // therefore hold every table and column of its model, or the database is refused before anything is applied.
    internal async Task RefuseEarlierBaselineAsync(CancellationToken cancellationToken = default)
    {
        var atBaseline = new List<(string Stream, string Baseline, IModel Model)>();
        foreach (var stream in SystemAndPlugins)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = stream.ResolveContext(scope.ServiceProvider);
            if (context.Database.GetMigrations().FirstOrDefault() is not { } baseline
                || !(await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Contains(baseline, StringComparer.Ordinal)
                || (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                continue;
            }

            atBaseline.Add((stream.Name, baseline, context.GetService<IDesignTimeModel>().Model));
        }

        if (atBaseline.Count == 0)
        {
            return;
        }

        // After every history: another process may be creating the database, and commits a stream's tables with or before
        // the row of its history, so a catalog read later holds the tables of each history read here (#3528).
        var database = await ReadDatabaseAsync(cancellationToken);
        var refused = new List<string>();
        var slow = new List<(string Stream, string Baseline, string Indexes)>();
        foreach (var (stream, baseline, model) in atBaseline)
        {
            var indexes = IndexesMissingFrom(database, model);
            var missing = MissingFrom(database, model);
            missing.AddRange(indexes.Where(index => index.IsUnique).Select(index => $"index {index.Name}"));
            if (missing.Count > 0)
            {
                refused.Add($"stream {stream} has applied its baseline {baseline} but lacks {string.Join(", ", missing)}");
            }

            if (indexes.Where(index => !index.IsUnique).Select(index => index.Name).ToList() is { Count: > 0 } slower)
            {
                slow.Add((stream, baseline, string.Join(", ", slower)));
            }
        }

        if (refused.Count > 0)
        {
            throw new FluentCmsConfigurationException(
                StorageErrorCodes.EarlierBaseline,
                $"{string.Join("; ", refused)}; nothing was applied: the database was made by an earlier build of the baseline, which a preview does not upgrade, so recreate it (on SQLite, delete its file) and apply the migrations again");
        }

        foreach (var (name, baseline, indexes) in slow)
        {
            MigrationLog.BaselineIndexesMissing(logger, name, baseline, indexes);
        }
    }

    // An index of the model the database lacks on a table it has. A missing unique index would let rows in that the model
    // refuses, so it is refused with the columns; any other only makes what it serves read more rows, so the host starts with
    // a warning rather than be refused for it (#3407). Adding it to the database would take a migration, which a preview
    // does not have.
    private static List<(string Name, bool IsUnique)> IndexesMissingFrom(DatabaseSchema database, IModel model)
    {
        var missing = new List<(string Name, bool IsUnique)>();
        var tables = model.GetEntityTypes()
            .Where(entity => entity.GetViewName() is null && !entity.IsTableExcludedFromMigrations() && entity.GetTableName() is not null)
            .GroupBy(entity => entity.GetTableName()!, StringComparer.Ordinal);
        foreach (var table in tables)
        {
            if (table.Key == StorageNames.SchemaHistory || !database.Tables.TryGetValue(table.Key, out var found))
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table.Key, table.First().GetSchema());
            foreach (var index in table.SelectMany(entity => entity.GetIndexes()))
            {
                if (index.GetDatabaseName(store) is { } name
                    && !found.Indexes.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                    && !missing.Any(other => other.Name == name))
                {
                    missing.Add((name, index.IsUnique));
                }
            }
        }

        return [.. missing.OrderBy(index => index.Name, StringComparer.Ordinal)];
    }

    // A table of the model the database lacks, or a column of one it has, as table.column. The columns of the schema history
    // are not asked for: a history that cannot be read or written is logged and the start goes on, so a column folded into
    // that table later is not refused here.
    private static List<string> MissingFrom(DatabaseSchema database, IModel model)
    {
        var missing = new List<string>();
        var tables = model.GetEntityTypes()
            .Where(entity => entity.GetViewName() is null && !entity.IsTableExcludedFromMigrations() && entity.GetTableName() is not null)
            .GroupBy(entity => entity.GetTableName()!, StringComparer.Ordinal);
        foreach (var table in tables)
        {
            if (!database.Tables.TryGetValue(table.Key, out var found))
            {
                missing.Add(table.Key);
                continue;
            }

            if (table.Key == StorageNames.SchemaHistory)
            {
                continue;
            }

            var columns = found.Columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var store = StoreObjectIdentifier.Table(table.Key, table.First().GetSchema());
            missing.AddRange(table
                .SelectMany(entity => entity.GetProperties())
                .Select(property => property.GetColumnName(store))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Where(column => !columns.Contains(column))
                .Select(column => $"{table.Key}.{column}"));
        }

        return [.. missing.Order(StringComparer.Ordinal)];
    }

    private async Task<DatabaseSchema> ReadDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = System.ResolveContext(scope.ServiceProvider);
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await provider.SchemaReader.ReadAsync(context.Database.GetDbConnection(), cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    // Every plugin's tables belong to its own stream: the plugins of the plugin host and the streams registered through the seam.
    private List<string> PluginTablePrefixes(IServiceProvider services) =>
        [.. _plugins.Select(plugin => plugin.Name)
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

    // The checks read what a running migration locks, the history tables on PostgreSQL and the catalog on SQL Server, so a
    // run takes the lock first. EF takes its own in the session that migrates, so the system context's is released before
    // anything is applied: held across MigrateAsync it would keep EF waiting for this process.
    private async Task<IReadOnlyList<StreamMigration>> CheckAndApplyAsync(IEnumerable<Stream> streams, CancellationToken cancellationToken)
    {
        await using var run = await WaitForSessionLockAsync(provider.LockMigrationsAsync, stream: null, DatabaseMigrationsLock, cancellationToken);
        await using (var scope = scopes.CreateAsyncScope())
        {
            var context = System.ResolveContext(scope.ServiceProvider);
            await using var checking = await WaitForSessionLockAsync(token => provider.TakeMigrationsLockAsync(context, token), stream: null, EfApplicationLock, cancellationToken);
            await RefuseMigrationsBeforeBaselineAsync(cancellationToken);
            await RefuseEarlierBaselineAsync(cancellationToken);
        }

        return await ApplyAsync(streams, cancellationToken);
    }

    private async Task<IReadOnlyList<StreamMigration>> ApplyAsync(IEnumerable<Stream> streams, CancellationToken cancellationToken)
    {
        var applied = new List<StreamMigration>();
        foreach (var stream in streams)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = stream.ResolveContext(scope.ServiceProvider);

            // Before the pending migrations are read, which another process's migration can block and can change.
            await using var taken = await WaitForSessionLockAsync(token => provider.TakeMigrationsLockAsync(context, token), stream.Name, EfApplicationLock, cancellationToken);

            // Migrating with nothing to do would still create the history table.
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                continue;
            }

            await WaitForMigrationsLockAsync(context, stream.Name, cancellationToken);
            MigrationLog.ApplyingStream(logger, pending.Count, stream.Name);
            using var recorder = AppliedMigrations.Record(context.GetService<IMigrator>());
            await context.Database.MigrateAsync(cancellationToken);
            applied.AddRange(recorder.Ids.Select(id => new StreamMigration(stream.Name, id)));
        }

        return applied;
    }

    // EF waits for its migrations lock without end, and on SQLite the lock is a row that a process stopped while it migrated
    // leaves behind, so the runner waits for the row to go first, with a bound. It does not clear the row: a `migrate` beside
    // the host may still be applying a long migration. A process that takes the lock after the last read is working, and EF
    // waits for it as before.
    private async Task WaitForMigrationsLockAsync(DbContext context, string stream, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        var poll = FirstMigrationsLockPoll;
        var logged = false;
        while (await provider.ReadMigrationsLockAsync(context, cancellationToken) is { } written)
        {
            // Before the bound, so that a wait that ends in the timeout has always logged it.
            var waited = _time.GetElapsedTime(started);
            var row = $"EF Core's migrations lock, the row of __EFMigrationsLock written at {written}";
            if (!logged && waited >= MigrationsLockWaitLogged)
            {
                MigrationLog.WaitingForMigrationsLock(logger, stream, row, MigrationsLockWait.TotalMinutes, MigrationsLockFix);
                logged = true;
            }

            if (waited >= MigrationsLockWait)
            {
                throw MigrationsLockTimeout(row, stream, MigrationsLockFix);
            }

            // Each read is logged by EF, so a wait that has been reported looks less often.
            await Task.Delay(poll, _time, cancellationToken);
            poll = logged ? ReportedMigrationsLockPoll : poll * 2 < LongestMigrationsLockPoll ? poll * 2 : LongestMigrationsLockPoll;
        }
    }

    private const string MigrationsLockFix =
        "the time is when the lock was taken, and it is never renewed; a host that starts or a 'fluentcms migrate' holds the " +
        "lock while it applies migrations, and one that stopped while it migrated left the row behind; check that no other host " +
        "and no 'fluentcms migrate' runs on the database, then delete the row with DELETE FROM \"__EFMigrationsLock\" and run " +
        "again; a migration that rebuilds a table may have been partly applied, so if the next run fails on a table or column " +
        "that already exists, restore the backup taken before the upgrade; a migration without a rebuild rolls back whole";

    private const string EfApplicationLock = "EF Core's migrations lock, the application lock __EFMigrationsLock that another session holds";

    private const string DatabaseMigrationsLock = "the migrations lock of FluentCMS, which another session of the database holds";

    private const string SessionLockFix =
        "another process is applying migrations: a host that starts or a 'fluentcms migrate' holds this lock while it applies " +
        "migrations, and the lock ends with its database session, so a process that stopped did not leave it behind; let that " +
        "process finish and run again, and if it does not finish, find the session that holds the lock on the database server " +
        "and what it runs";

    // A null stream is the wait of a whole run, before it has read what any stream has pending.
    private static TimeoutException MigrationsLockTimeout(string held, string? stream, string fix, Exception? inner = null) => new(
        $"{held}, was not released within {MigrationsLockWait.TotalMinutes:0} minutes, so " +
        $"{(stream is null ? "this run migrated no stream" : $"the {stream} stream was not migrated")}; {fix}.", inner);

    // EF waits for a lock that ends with its holder's session no longer than a command may run, 30 seconds by default, and its
    // timeout names nothing, and the lock of a whole run has no bound at all. So a provider takes such a lock with no timeout of
    // its own, and the runner bounds the wait as it bounds the wait for SQLite's row. A lock granted as the wait gives up is kept.
    private async Task<IAsyncDisposable?> WaitForSessionLockAsync(
        Func<CancellationToken, Task<IAsyncDisposable?>> take, string? stream, string held, CancellationToken cancellationToken)
    {
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var taking = take(giveUp.Token);
        var started = _time.GetTimestamp();
        var poll = FirstMigrationsLockPoll;
        var logged = false;
        while (!taking.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            var waited = _time.GetElapsedTime(started);
            if (!logged && waited >= MigrationsLockWaitLogged)
            {
                MigrationLog.WaitingForMigrationsLock(logger, stream ?? SystemStream, held, MigrationsLockWait.TotalMinutes, SessionLockFix);
                logged = true;
            }

            if (waited >= MigrationsLockWait)
            {
                await giveUp.CancelAsync();
                break;
            }

            await Task.WhenAny(taking, Task.Delay(poll, _time, cancellationToken));
            poll = poll * 2 < LongestMigrationsLockPoll ? poll * 2 : LongestMigrationsLockPoll;
        }

        try
        {
            return await taking is { } taken ? new SessionLock(taken, logger) : null;
        }
        catch (Exception exception) when (giveUp.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw MigrationsLockTimeout(held, stream, SessionLockFix, exception);
        }
    }

    // A release that threw would replace the outcome of what ran under the lock.
    private sealed class SessionLock(IAsyncDisposable taken, ILogger logger) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await taken.DisposeAsync();
            }
            catch (Exception exception)
            {
                MigrationLog.MigrationsLockNotReleased(logger, exception.GetType().Name, exception);
            }
        }
    }

    // With whether it recorded the schema history, which it does when it applied every change and, for a host that schema
    // apply serves, the history says none is lost.
    private async Task<(SchemaSyncReport Report, bool Recorded)> SyncAsync(SchemaModel target, CancellationToken cancellationToken)
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
            await using var held = await locks.TryAcquireAsync(SchemaSyncLock, SchemaSyncWait, cancellationToken) ?? throw SchemaSyncBusy();
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

        var recorded = diff.NotApplied.Count == 0 && await _history.RecordAfterSyncAsync(target, UsesSchemaApplyAsync, cancellationToken);
        return (new SchemaSyncReport(applied, diff.NotApplied), recorded);
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
