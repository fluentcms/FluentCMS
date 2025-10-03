namespace FluentCMS.Repositories.EntityFramework.Services;

public interface ISchemaValidatorService
{
    /// <summary>
    /// Validates the database schema to ensure it matches the expected structure.
    /// </summary>
    Task Initialize(CancellationToken cancellationToken = default);
}
