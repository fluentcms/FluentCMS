using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Configuration;

internal sealed class OptionsDbSeeder(ILogger<OptionsDbSeeder> logger, IOptionsCatalog registrations, DbConfigurationSource source) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await source.Repository.EnsureCreated(cancellationToken);

            foreach (var reg in registrations.All)
            {
                try
                {
                    var rows = await source.Repository.Upsert(reg, cancellationToken);
                    if (rows > 0)
                    {
                        logger.LogInformation("Seeded options for {Section}", reg.Section);
                    }
                }
                catch (Exception)
                {
                    logger.LogWarning("Failed seeding options for {Section}", reg.Section);
                }
            }

            // After seeding, refresh the configuration provider so IOptions binds from DB
            // Check if provider is initialized to avoid race condition
            try
            {
                if (source.Provider != null)
                {
                    source.Provider.TriggerReload();
                    logger.LogDebug("Configuration provider reloaded after seeding");
                }
                else
                {
                    logger.LogWarning("Configuration provider not yet initialized, skipping reload trigger");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to trigger configuration reload after seeding");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed seeding options DB");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
