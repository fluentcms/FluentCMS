
namespace FluentCMS.Repositories.EFCore;

public class HttpLogRepository(FluentCmsDbContext dbContext) : IHttpLogRepository
{
    public async Task CreateMany(IEnumerable<HttpLog> httpLogs, CancellationToken cancellationToken = default)
    {
        foreach (var http in httpLogs)
        {
            if (http.Id == Guid.Empty)
                http.Id = Guid.NewGuid();
        }
        await dbContext.HttpLogs.AddRangeAsync(httpLogs, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
