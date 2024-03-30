using FluentCMS.Repositories.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using File = FluentCMS.Entities.File;

namespace FluentCMS.Repositories.MongoDB;
public class FileRepository(IMongoDBContext mongoDbContext, IAuthContext authContext)
    : AuditableEntityRepository<File>(mongoDbContext, authContext), IFileRepository
{
    public async Task<File?> GetBySlug(string slug, CancellationToken cancellationToken = default)
    {
        return await (await Collection.FindAsync(x => x.Slug == slug, cancellationToken: cancellationToken)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<File?> Delete(string slug, CancellationToken cancellationToken = default)
    {
        return await Collection.FindOneAndDeleteAsync(x => x.Slug == slug, cancellationToken: cancellationToken);
    }
}
