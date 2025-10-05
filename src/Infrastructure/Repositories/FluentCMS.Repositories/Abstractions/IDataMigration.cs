namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Defines a contract for performing data migrations.
public interface IDataMigration
{
    /// <summary>
    /// Gets the priority of the migration. Lower numbers execute first.
    /// Use gaps (10, 20, 30) to allow future insertion without reordering.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Performs the migration.
    /// </summary>
    Task Migrate(CancellationToken cancellationToken = default);
}
