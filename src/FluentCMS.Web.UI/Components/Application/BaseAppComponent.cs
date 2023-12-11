using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;
using System.Reflection;

namespace FluentCMS.Web.UI.Components.Application;

public class BaseAppComponent : ComponentBase
{
    [CascadingParameter]
    public AppState AppState { get; set; } = default!;
    [Inject]
    public IHttpContextAccessor HttpContextAccessor { get; set; }
    [Inject]
    public IMemoryCache MemoryCache { get; set; }
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        // if is server-side prerendering
        if (!HttpContextAccessor.HttpContext!.Response.HasStarted)
        {
            // call OnPreRendering to run any server-side prerendering logic
            await OnPreRendering();

            // cache final state of CachedValues
            CacheValues();
        }
        else
        {
            // restore final state of CachedValues
            RestoreValues();
        }
    }
    public virtual async Task OnPreRendering()
    {
        await Task.CompletedTask;
    }
    public virtual void CacheValues()
    {
        var properties = GetType().GetProperties().Where(p => p.GetCustomAttribute<MemoryCachedValueAttribute>() != null);
        foreach (var property in properties)
        {
            MemoryCache.Set($"{GetType().FullName}.{property.Name}", property.GetValue(this), new MemoryCacheEntryOptions()
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });
        }
    }
    public virtual void RestoreValues()
    {
        var properties = GetType().GetProperties().Where(p => p.GetCustomAttribute<MemoryCachedValueAttribute>() != null);
        foreach (var property in properties)
        {
            property.SetValue(this, MemoryCache.Get($"{GetType().FullName}.{property.Name}"));
        }
    }
}
