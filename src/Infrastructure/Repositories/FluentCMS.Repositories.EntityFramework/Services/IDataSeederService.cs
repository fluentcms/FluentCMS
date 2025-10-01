namespace FluentCMS.Repositories.EntityFramework.Services;

public interface IDataSeederService
{
    /// <summary>
    /// Seeds the database with initial data if necessary.
    ///  </summary>
    Task Initialize(CancellationToken cancellationToken = default);
}
