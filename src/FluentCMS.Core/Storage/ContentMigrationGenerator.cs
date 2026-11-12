using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Storage;

public sealed record GeneratedMigration(string Id, string MigrationCode, string MetadataCode, string SnapshotCode);

public sealed record GeneratedMigrationFiles(string MigrationFile, string MetadataFile, string SnapshotFile);

// The files of the migration that was removed, and the snapshot, which is written back as the previous migration left it
// or deleted with the first.
public sealed record RemovedMigrationFiles(string? MigrationFile, string? MetadataFile, string? SnapshotFile);

// The design-time services behind `fluentcms migrations add` and `remove`. They need Microsoft.EntityFrameworkCore.Design, which
// this package does not pass on to a host: only the CLI and tests carry it, so only they can call this class.
public static class ContentMigrationGenerator
{
    private const string SubNamespace = "Migrations.Content";

    // Takes a factory because a context that has already called HasPendingModelChanges() silently scaffolds
    // CreateTable for every table, so each generation must start from a context nothing else has touched.
    public static GeneratedMigration Scaffold(Func<CmsDbContext> createContext, string name, string rootNamespace)
    {
        ArgumentNullException.ThrowIfNull(createContext);

        return WithScaffolder(createContext, scaffolder =>
        {
            var migration = ScaffoldMarked(scaffolder, name, rootNamespace);
            return new GeneratedMigration(migration.MigrationId, migration.MigrationCode, migration.MetadataCode, migration.SnapshotCode);
        });
    }

    // Writes under Migrations/Content in the project, or next to the snapshot if the project has moved it.
    public static GeneratedMigrationFiles Add(Func<CmsDbContext> createContext, string name, string rootNamespace, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(createContext);

        return WithScaffolder(createContext, scaffolder =>
        {
            var files = scaffolder.Save(projectDirectory, ScaffoldMarked(scaffolder, name, rootNamespace), outputDir: null);
            return new GeneratedMigrationFiles(files.MigrationFile!, files.MetadataFile!, files.SnapshotFile!);
        });
    }

    // The id of the newest content migration of the factory's assembly, or null when it has none.
    public static string? Newest(Func<CmsDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);

        using var context = createContext();
        return context.Database.GetMigrations().LastOrDefault();
    }

    // Deletes the newest content migration and puts the snapshot back. The caller has checked that the database does not
    // have it: EF's own check would open the factory's connection, and with force it would revert the migration there,
    // so the history it reads here is empty and the migrator refuses any use.
    public static RemovedMigrationFiles Remove(Func<CmsDbContext> createContext, string rootNamespace, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(createContext);

        // EF names the snapshot only when it deletes it, not when it writes it back.
        string? snapshot;
        using (var context = createContext())
        {
            snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot?.GetType().Name is { } name
                ? Directory.EnumerateFiles(projectDirectory, $"{name}.cs", SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }

        return WithScaffolder(
            createContext,
            scaffolder =>
            {
                var files = scaffolder.RemoveMigration(projectDirectory, rootNamespace, force: false, language: null, dryRun: false);
                return new RemovedMigrationFiles(files.MigrationFile, files.MetadataFile, files.SnapshotFile ?? snapshot);
            },
            services =>
            {
                services.AddSingleton(OfflineDatabase.Create<IHistoryRepository>());
                services.AddSingleton(OfflineDatabase.Create<IMigrator>());
            });
    }

    private static T WithScaffolder<T>(Func<CmsDbContext> createContext, Func<IMigrationsScaffolder, T> use, Action<IServiceCollection>? replace = null)
    {
        using var context = createContext();

        var services = new ServiceCollection();
        services.AddEntityFrameworkDesignTimeServices();
        services.AddDbContextDesignTimeServices(context);

        var providerAssembly = Assembly.Load(new AssemblyName(context.GetService<IDatabaseProvider>().Name));
        var attribute = providerAssembly.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException($"{providerAssembly.GetName().Name} declares no design-time services.");
        var designTimeServices = (IDesignTimeServices)Activator.CreateInstance(providerAssembly.GetType(attribute.TypeName, throwOnError: true)!)!;
        designTimeServices.ConfigureDesignTimeServices(services);
        replace?.Invoke(services);

        using var provider = services.BuildServiceProvider();
        return use(provider.GetRequiredService<IMigrationsScaffolder>());
    }

    // EF marks only the designer and snapshot files, and the composite index columns in the migration file fail a
    // build with CA1861.
    private static ScaffoldedMigration ScaffoldMarked(IMigrationsScaffolder scaffolder, string name, string rootNamespace)
    {
        var migration = scaffolder.ScaffoldMigration(name, rootNamespace, SubNamespace);

        return new ScaffoldedMigration(
            migration.FileExtension,
            migration.PreviousMigrationId,
            "// <auto-generated />" + Environment.NewLine + migration.MigrationCode,
            migration.MigrationId,
            migration.MetadataCode,
            migration.MigrationSubNamespace,
            migration.SnapshotCode,
            migration.SnapshotName,
            migration.SnapshotSubnamespace);
    }
}

// Stands in for what would open the database at design time: an empty history, and nothing else.
internal class OfflineDatabase : DispatchProxy
{
    public static T Create<T>()
        where T : class => Create<T, OfflineDatabase>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IHistoryRepository.GetAppliedMigrations) => Array.Empty<HistoryRow>(),
        nameof(IHistoryRepository.GetAppliedMigrationsAsync) => Task.FromResult<IReadOnlyList<HistoryRow>>([]),
        _ => throw new InvalidOperationException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} would open the database, which removing a migration never does."),
    };
}
