using FluentCMS.Core.DependencyInjection;
using FluentCMS.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Seeding;

// cms.Seeding: the seeders of the host. With none, nothing is registered and start-up does no extra work.
public sealed class SeedingBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Type> _types = [];
    private readonly List<string> _locales = [];

    internal SeedingBuilder(IServiceCollection services)
    {
        _services = services;
    }

    // Whether the host runs the seeders when it starts. Turn it off to run them with ISeedRunner at another time.
    public bool RunOnStart { get; set; } = true;

    // How long a seeder counts as running after an instance claimed it. A claim older than this is taken over, so that
    // an instance that died during a run does not block the seed for ever.
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(15);

    internal IReadOnlyList<Type> Types => _types;

    internal IReadOnlyList<string> Locales => _locales;

    public SeedingBuilder Add<T>()
        where T : class, ISeeder
    {
        if (!_types.Contains(typeof(T)))
        {
            _types.Add(typeof(T));
            _services.AddTransient<T>();
        }

        return this;
    }

    // A locale the default site must allow before the seeders run, for seed content in more than the default locale. It
    // needs at least one seeder added with Add, or the host refuses to start. Until sites are managed through the API (M2)
    // this is the only way to add a locale to the default site.
    public SeedingBuilder RequireLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(locale);

        if (!_locales.Contains(locale, StringComparer.OrdinalIgnoreCase))
        {
            _locales.Add(locale);
        }

        return this;
    }

    internal void Validate()
    {
        if (ClaimTimeout <= TimeSpan.Zero)
        {
            throw new FluentCmsConfigurationException(SeedingErrorCodes.InvalidTimeout, $"{nameof(ClaimTimeout)} must be positive.");
        }

        if (_locales.Count > 0 && _types.Count == 0)
        {
            throw new FluentCmsConfigurationException(
                SeedingErrorCodes.LocaleWithoutSeeder,
                "cms.Seeding.RequireLocale is set but no seeder was added with cms.Seeding.Add, so nothing would use the locale.");
        }
    }
}
