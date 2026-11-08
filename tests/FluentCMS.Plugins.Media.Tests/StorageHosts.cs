using System.Collections.Concurrent;
using System.Security.Cryptography;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media.Tests;

// A media host whose storage is given, local disk in a directory of the test's, with a signing key of its own so that two
// hosts on one database file sign alike. The job runner has no workers.
internal static class StorageHosts
{
    public static string SigningKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static FluentCmsTestHost Create(DirectoryInfo local, string signingKey, Action<MediaPlugin>? storage = null, string? database = null, CapturedLogs? logs = null)
    {
        var host = new FluentCmsTestHost()
            .WithServices(services =>
            {
                services.PostConfigure<MediaOptions>(options =>
                {
                    options.Local.Root = local.FullName;
                    options.SigningKey = signingKey;
                });
                if (logs is not null)
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
                }
            })
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                if (storage is not null)
                {
                    cms.AddPlugin(storage);
                }
            });
        return database is null ? host : host.UseSqlite(database);
    }

    public static async Task<HttpClient> AdminAsync(FluentCmsTestHost host) =>
        host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: MediaHost.Token));
}

// Every line the host logs, with its exception.
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category} {eventId.Id}: {formatter(state, exception)}{(exception is null ? "" : "\n" + exception)}");
    }
}
