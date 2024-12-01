using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Services;

public class BackgroundHttpLogProcessor(IHttpLogChannel logChannel, IServiceProvider serviceProvider, IOptions<HttpLogConfig> options) : BackgroundService
{
    private readonly HttpLogConfig _config = options.Value ?? new HttpLogConfig();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enable)
            return;

        var batch = new List<HttpLog>();

        try
        {
            await foreach (var log in logChannel.ReadAllAsync(stoppingToken))
            {
                batch.Add(log);

                // Process the batch when the size reaches the limit
                if (batch.Count >= _config.BatchSize)
                {
                    await ProcessBatchAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful exit on cancellation
        }
        catch (Exception)
        {
            // Log or handle unexpected errors
            // TODO: Add logging
        }

        // Process remaining logs before shutdown
        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(IEnumerable<HttpLog> batch, CancellationToken cancellationToken)
    {
        try
        {
            // Create a new scope for each log entry
            using var scope = serviceProvider.CreateScope();
            var httpLogRepository = scope.ServiceProvider.GetRequiredService<IHttpLogRepository>();
            await httpLogRepository.CreateMany(batch, cancellationToken);
        }
        catch (Exception)
        {
            // Handle errors during batch processing, e.g., log or retry logic
            // TODO: Add logging
        }
    }
}
