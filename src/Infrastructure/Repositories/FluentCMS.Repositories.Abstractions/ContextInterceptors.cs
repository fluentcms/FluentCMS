using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// DB-agnostic interceptor for data context operations
/// </summary>
public interface IContextInterceptor
{
    /// <summary>
    /// Called before SaveChanges is executed
    /// </summary>
    Task OnBeforeSaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after SaveChanges completes successfully
    /// </summary>
    Task OnAfterSaveChangesAsync(int affectedRows, CancellationToken cancellationToken = default);
}

/// <summary>
/// Composite context that applies interceptors around operations
/// </summary>
public class InterceptorDataContext<TArea> : IDataContext<TArea>
    where TArea : IDatabaseArea
{
    private readonly IDataContext<TArea> _inner;
    private readonly IReadOnlyList<IContextInterceptor> _interceptors;

    public InterceptorDataContext(IDataContext<TArea> inner, IReadOnlyList<IContextInterceptor> interceptors)
    {
        _inner = inner;
        _interceptors = interceptors;
    }

    // IEntitySet<TEntity> IDataContext.Set<TEntity>() where TEntity : class
    IEntitySet<TEntity> IDataContext.Set<TEntity>() where TEntity : class
    {
        // For entity set operations, we could add separate interceptor hooks later
        // For now, delegate directly
        return _inner.Set<TEntity>();
    }

    // Task<int> IDataContext.SaveChanges(CancellationToken cancellationToken)
    Task<int> IDataContext.SaveChanges(CancellationToken cancellationToken = default)
    {
        // Chain interceptor calls like middleware pipeline
        return ExecuteInterceptorsAsync(cancellationToken);
    }

    private async Task<int> ExecuteInterceptorsAsync(CancellationToken cancellationToken)
    {
        // Call all BeforeSaveChanges interceptors
        foreach (var interceptor in _interceptors)
        {
            await interceptor.OnBeforeSaveChangesAsync(cancellationToken);
        }

        // Execute the actual SaveChanges
        var affectedRows = await _inner.SaveChanges(cancellationToken);

        // Call all AfterSaveChanges interceptors
        foreach (var interceptor in _interceptors)
        {
            await interceptor.OnAfterSaveChangesAsync(affectedRows, cancellationToken);
        }

        return affectedRows;
    }
}

/// <summary>
/// DTO for interceptor registration
/// </summary>
public class InterceptorRegistration
{
    public Type AreaType { get; set; } = null!;
    public Type InterceptorType { get; set; } = null!;
}

/// <summary>
/// Registry for storing interceptor types per database area
/// </summary>
public class InterceptorRegistry
{
    private readonly ConcurrentDictionary<Type, List<Type>> _areaInterceptors = new();
    private bool _initialized = false;

    public void Initialize(IEnumerable<InterceptorRegistration> registrations)
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var reg in registrations)
        {
            Register(reg.AreaType, reg.InterceptorType);
        }
    }

    public void Register(Type areaType, Type interceptorType)
    {
        var list = _areaInterceptors.GetOrAdd(areaType, _ => new List<Type>());
        if (!list.Contains(interceptorType))
        {
            list.Add(interceptorType);
        }
    }

    public IReadOnlyList<Type> GetInterceptors<TArea>(IServiceProvider services) where TArea : IDatabaseArea
    {
        Initialize(services.GetServices<InterceptorRegistration>());
        var areaType = typeof(TArea);
        _areaInterceptors.TryGetValue(areaType, out var list);
        return list ?? (IReadOnlyList<Type>)Array.Empty<Type>();
    }
}

/// <summary>
/// Service collection extensions for registering context interceptors
/// </summary>
public static class ContextInterceptorExtensions
{
    /// <summary>
    /// Register an interceptor type for a specific data context by marking area
    /// </summary>
    public static IServiceCollection AddDataContextInterceptor<TContext, TInterceptor>(this IServiceCollection services)
        where TContext : IDataContext
        where TInterceptor : class, IContextInterceptor
    {
        // Extract area type from context generic argument
        var areaType = typeof(TContext).GetGenericArguments().FirstOrDefault();
        if (areaType == null || !typeof(IDatabaseArea).IsAssignableFrom(areaType))
        {
            throw new InvalidOperationException($"Context {typeof(TContext).Name} must be a generic IDataContext<TArea> where TArea implements IDatabaseArea");
        }

        services.TryAddSingleton<InterceptorRegistry>();
        services.AddTransient<TInterceptor>();
        services.AddSingleton<InterceptorRegistration>(new InterceptorRegistration { AreaType = areaType, InterceptorType = typeof(TInterceptor) });

        return services;
    }


}

/// <summary>
/// Extensions for creating interceptor-wrapped data contexts
/// </summary>
public static class InterceptorDataContextExtensions
{
    public static IDataContext<TArea> WithInterceptors<TArea>(this IDataContext<TArea> context, IServiceProvider services, IReadOnlyList<Type> interceptorTypes)
        where TArea : IDatabaseArea
    {
        if (interceptorTypes.Count == 0)
            return context;

        // Resolve all interceptor instances (order preserved)
        var interceptors = new List<IContextInterceptor>();
        foreach (var type in interceptorTypes)
        {
            var instance = (IContextInterceptor)services.GetRequiredService(type);
            interceptors.Add(instance);
        }

        return new InterceptorDataContext<TArea>(context, interceptors);
    }
}
