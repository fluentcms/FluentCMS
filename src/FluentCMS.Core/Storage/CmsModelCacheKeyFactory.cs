using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Core.Storage;

internal sealed class CmsModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context switch
    {
        CmsDbContext cms => (context.GetType(), cms.ModelKey, cms.ConventionsKey, cms.JsonDialectKey, designTime),
        SystemDbContext system => (context.GetType(), system.ConventionsKey, designTime),
        _ => (context.GetType(), designTime),
    };
}
