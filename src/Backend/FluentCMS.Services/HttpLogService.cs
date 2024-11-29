using FluentCMS.Entities.Logging;

namespace FluentCMS.Services;

public interface IHttpLogService : IAutoRegisterService
{
    Task Log(HttpLog httpLog, CancellationToken cancellationToken = default);
}

public class HttpLogService (IHttpLogRepository httpLogRepository) : IHttpLogService
{
    public async Task Log(HttpLog httpLog, CancellationToken cancellationToken = default)
    {
        await httpLogRepository.Create(httpLog, cancellationToken);
    }
}
