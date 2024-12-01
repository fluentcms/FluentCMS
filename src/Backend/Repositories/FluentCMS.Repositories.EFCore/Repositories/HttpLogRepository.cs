namespace FluentCMS.Repositories.EFCore;

public class HttpLogRepository(FluentCmsDbContext dbContext) : IHttpLogRepository
{
    public async Task Create(HttpLog log, CancellationToken cancellationToken = default)
    {
        if (log.Id == Guid.Empty)
            log.Id = Guid.NewGuid();

        await dbContext.HttpLogs.AddAsync(log, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
