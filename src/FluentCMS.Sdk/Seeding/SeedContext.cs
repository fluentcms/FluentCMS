using FluentCMS.Content;
using FluentCMS.Sites;

namespace FluentCMS.Seeding;

/// <summary>The operation a seeder runs in, and the services it seeds with.</summary>
public sealed class SeedContext
{
    private readonly IOperationContextAccessor _accessor;

    /// <summary>Creates a context over a scope whose operation is already set.</summary>
    /// <param name="services">The services of the scope the seeder runs in.</param>
    public SeedContext(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Services = services;
        _accessor = Get<IOperationContextAccessor>();
        Time = services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
    }

    /// <summary>The services of the scope the seeder runs in.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The clock of the host. Use it, not the system clock, to pick an instant to schedule.</summary>
    public TimeProvider Time { get; }

    /// <summary>The site being seeded.</summary>
    public SiteId Site => Current.Site;

    /// <summary>The locale that writes use now.</summary>
    public string Locale => Current.Locale;

    /// <summary>Gets a service of the scope.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service.</returns>
    public T Get<T>()
        where T : class => Services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"There is no service {typeof(T).Name} in the scope of the seeder.");

    /// <summary>Makes writes use another locale until the result is disposed, such as to add a translation.</summary>
    /// <param name="locale">The locale, which must be one of the site's.</param>
    /// <returns>An object that puts the previous locale back when disposed.</returns>
    public IDisposable InLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);

        var previous = Current;
        _accessor.Current = previous with { Locale = locale };
        return new Restore(_accessor, previous);
    }

    private OperationContext Current => _accessor.Current
        ?? throw new InvalidOperationException("A seeder runs inside an operation; use ISeedRunner to run one.");

    private sealed class Restore(IOperationContextAccessor accessor, OperationContext previous) : IDisposable
    {
        public void Dispose() => accessor.Current = previous;
    }
}
