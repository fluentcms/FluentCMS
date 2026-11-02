using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Core.Storage;

internal sealed class CmsModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is CmsDbContext cms
        ? (context.GetType(), cms.ModelKey, cms.ConventionsType, designTime)
        : (context.GetType(), designTime);
}
