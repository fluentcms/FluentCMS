using Microsoft.Extensions.Hosting;
using System.Threading.Channels;

namespace FluentCMS.Services;

/// <summary>
/// Interface for logging HTTP logs.
/// </summary>
public interface IHttpLogService : IAutoRegisterService
{
    /// <summary>
    /// Enqueues a log entry for background processing.
    /// </summary>
    /// <param name="httpLog">The HTTP log entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    void Log(HttpLog httpLog, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of the HTTP log service using a background queue for logging.
/// </summary>
public class HttpLogService : IHttpLogService, IHostedService
{
    private readonly IHttpLogRepository _httpLogRepository;
    private readonly Channel<HttpLog> _logChannel;
    private Task? _processingTask;
    private readonly CancellationTokenSource _cts;
    private readonly int _batchSize = 10; // Batch size for processing logs

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpLogService"/> class.
    /// </summary>
    /// <param name="httpLogRepository">The HTTP log repository.</param>
    public HttpLogService(IHttpLogRepository httpLogRepository)
    {
        _httpLogRepository = httpLogRepository;
        _logChannel = Channel.CreateUnbounded<HttpLog>();
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Enqueues a log entry for background processing.
    /// </summary>
    /// <param name="httpLog">The HTTP log entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void Log(HttpLog httpLog, CancellationToken cancellationToken = default)
    {
        if (!_logChannel.Writer.TryWrite(httpLog))
        {
            // Handle overflow or fallback logic if needed
            Console.WriteLine("Log channel is full. Log entry dropped.");
        }
    }

    /// <summary>
    /// Starts the background processing task.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processingTask = ProcessLogsAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the background processing task.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();

        if (_processingTask != null)
        {
            await _processingTask;
        }
    }

    /// <summary>
    /// Processes log entries in the background in batches.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task ProcessLogsAsync(CancellationToken cancellationToken)
    {
        var batch = new List<HttpLog>();

        try
        {
            await foreach (var log in _logChannel.Reader.ReadAllAsync(cancellationToken))
            {
                batch.Add(log);

                // Process batch when the size reaches the configured limit
                if (batch.Count >= _batchSize)
                {
                    await ProcessBatchAsync(batch, cancellationToken);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Gracefully exit on cancellation
        }
        catch (Exception ex)
        {
            // Handle unexpected exceptions
            Console.WriteLine($"Error in log processing: {ex.Message}");
        }

        // Process any remaining logs in the batch
        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch, cancellationToken);
        }
    }

    /// <summary>
    /// Processes a batch of log entries.
    /// </summary>
    /// <param name="batch">The batch of log entries.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task ProcessBatchAsync(IEnumerable<HttpLog> batch, CancellationToken cancellationToken)
    {
        try
        {
            await _httpLogRepository.CreateMany(batch, cancellationToken);
        }
        catch (Exception ex)
        {
            // Handle exceptions during batch processing
            Console.WriteLine($"Failed to process batch: {ex.Message}");
        }
    }
}
