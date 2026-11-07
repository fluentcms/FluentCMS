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

    public static string Of(IReadOnlyModel model) =>
        Annotated.FirstOrDefault(provider => model.GetAnnotations().Any(annotation => annotation.Name.StartsWith(provider.Prefix, StringComparison.Ordinal))).Name
        ?? Sqlite;
}
