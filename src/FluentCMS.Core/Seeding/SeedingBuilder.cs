using FluentCMS.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Seeding;

// cms.Seeding: the seeders that run when the host starts. With none, nothing is registered and start-up does no extra work.
public sealed class SeedingBuilder(IServiceCollection services)
{
    private readonly List<string> _locales = [];

    internal int Count { get; private set; }

    internal IReadOnlyList<string> Locales => _locales;

    public SeedingBuilder Add<T>()
        where T : class, ISeeder
    {
        services.AddTransient<ISeeder, T>();
        Count++;
        return this;
    }

    // A locale the default site must allow before the seeders run, for seed content in more than the default locale.
    // Until sites are managed through the API (M2) this is the only way to add one to the default site.
    public SeedingBuilder RequireLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);

        if (!_locales.Contains(locale, StringComparer.OrdinalIgnoreCase))
        {
            _locales.Add(locale);
        }

        return this;
    }
}
