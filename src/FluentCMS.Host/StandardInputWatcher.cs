namespace FluentCMS.Host;

// Stops the stdio host when standard input closes, which is how an MCP client ends the session.
public sealed class StandardInputWatcher(
    IHostApplicationLifetime lifetime,
    ILogger<StandardInputWatcher> logger,
    StandardInputSource source) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A blocked console read cannot be cancelled, so the loop runs detached and shutdown never waits for it.
        _ = Task.Run(() => ReadUntilClosedAsync(stoppingToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task ReadUntilClosedAsync(CancellationToken stoppingToken)
    {
        var buffer = new byte[4096];

        try
        {
            await using var input = source.Open();

            while (!stoppingToken.IsCancellationRequested)
            {
                // Input is discarded until the MCP transport (F-MCP) takes over standard input.
                if (await input.ReadAsync(buffer, stoppingToken) == 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Standard input failed; stopping.");
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Standard input closed; stopping.");
            lifetime.StopApplication();
        }
    }
}

public sealed record StandardInputSource(Func<Stream> Open)
{
    public static StandardInputSource Console { get; } = new(() => System.Console.OpenStandardInput());
}
