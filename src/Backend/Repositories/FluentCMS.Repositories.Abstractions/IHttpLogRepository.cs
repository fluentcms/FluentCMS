namespace FluentCMS.Repositories.Abstractions;

public interface IHttpLogRepository
{
    Task Create(HttpLog log, CancellationToken cancellationToken = default);
}
