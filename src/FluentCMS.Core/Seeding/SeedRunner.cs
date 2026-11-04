using FluentCMS.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Sites;
using FluentCMS.Seeding;
using FluentCMS.Sites;

namespace FluentCMS.Core.Seeding;

internal sealed class SeedRunner(
    IServiceProvider services,
    IEnumerable<ISeeder> seeders,
    IOperationContextAccessor accessor,
    LocaleRegistry locales,
    SiteManager sites,
    SeedingBuilder options,
    TimeProvider time) : ISeedRunner
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var all = seeders.ToList();
        if (all.Count == 0)
        {
            return;
        }

        var site = new SiteId(DefaultSite.Id);
        foreach (var required in options.Locales)
        {
            await sites.EnsureLocaleAsync(site.Value, required, cancellationToken);
        }

        var locale = (await locales.GetAsync(site, cancellationToken)).Default;
        var previous = accessor.Current;

        try
        {
            foreach (var seeder in all)
            {
                accessor.Current = new OperationContext(
                    Principal.System,
                    Origin.Seed,
                    site,
                    locale,
                    ClientId: null,
                    Guid.CreateVersion7().ToString(),
                    time.GetUtcNow());
                await seeder.SeedAsync(new SeedContext(services), cancellationToken);
            }
        }
        finally
        {
            accessor.Current = previous;
        }
    }
}
