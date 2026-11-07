using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluentCMS.Core.Storage;

internal sealed class CmsModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context switch
    {
        CmsDbContext cms => (context.GetType(), cms.ModelKey, cms.ConventionsKey, cms.JsonDialectKey, cms.PluginModelKey, designTime),
        SystemDbContext system => (context.GetType(), system.ConventionsKey, designTime),
        IPluginModelKey plugin => (context.GetType(), plugin.ModelKey, designTime),
        _ => (context.GetType(), designTime),
    };
}
