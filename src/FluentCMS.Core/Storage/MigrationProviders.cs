using FluentCMS.Core.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FluentCMS.Core.Storage;

// The provider a migration set was scaffolded for, from what EF writes on every model a provider builds and so on every
// snapshot and target model: PostgreSQL and SQL Server annotate the model with their value generation strategy, under
// Npgsql: and SqlServer:, and SQLite adds nothing. A model that maps no table, such as that of a hand-written migration,
// names none.
internal static class MigrationProviders
{
    private const string Sqlite = "SQLite";

    private static readonly (string Prefix, string Name)[] Annotated = [("Npgsql:", "PostgreSQL"), ("SqlServer:", "SQL Server")];

    // The provider of the context's migration set when it is not the one of the context's own model, or null. Reads the
    // migrations and the snapshot only, never the database.
    public static string? ForeignTo(DbContext context)
    {
        var own = Of(context.GetService<IDesignTimeModel>().Model);
        var migrations = context.GetService<IMigrationsAssembly>();
        var activeProvider = context.Database.ProviderName!;

        IEnumerable<IReadOnlyModel?> models =
        [
            migrations.ModelSnapshot?.Model,
            .. migrations.Migrations.Values.Select(type => migrations.CreateMigration(type, activeProvider).TargetModel),
        ];

        return models.OfType<IReadOnlyModel>()
            .Where(model => model.GetEntityTypes().Any())
            .Select(Of)
            .FirstOrDefault(scaffolded => scaffolded != own);
    }

    // Why the context's migrations cannot run on its provider, or null: they were scaffolded for another, or the plugin's
    // assembly holds sets per provider and none for this one.
    public static string? RefusalOf(DbContext context)
    {
        var sets = context.GetService<IMigrationsAssembly>() as PluginMigrationsAssembly;
        var missing = sets is { HasSet: false } ? $"has no set in a namespace ending in .{sets.Set}" : null;

        return ForeignTo(context) is { } other
            ? missing is null ? $"was scaffolded for {other}" : $"was scaffolded for {other} and {missing}"
            : sets is { PerProvider: true } ? missing : null;
    }

    public static string Of(IReadOnlyModel model) =>
        Annotated.FirstOrDefault(provider => model.GetAnnotations().Any(annotation => annotation.Name.StartsWith(provider.Prefix, StringComparison.Ordinal))).Name
        ?? Sqlite;
}
