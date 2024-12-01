namespace FluentCMS.Repositories.Abstractions;

public interface IHttpLogRepository
{
    Task CreateMany(IEnumerable<HttpLog> httpLogs, CancellationToken cancellationToken = default);
}
