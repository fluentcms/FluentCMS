using FluentCMS.Core.Identity;
using FluentCMS.Core.Security;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Infrastructure.Webhooks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.IntegrationTests.Webhooks;

// A migrated database on one provider with the webhook directory acting as whoever Initiator names. example.com and localhost:5005
// are on the outbound allow-list, secrets are protected with a real key ring, and everything logged is captured.
internal sealed class WebhookScenario : IAsyncDisposable
{
    public static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    public static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;
    private readonly CapturedLogs _logs;

    private WebhookScenario(TemporaryDatabase database, ServiceProvider services, SwitchableInitiator initiator, ManualClock clock, CapturedLogs logs)
    {
        _database = database;
        Services = services;
        _initiator = initiator;
        Clock = clock;
        _logs = logs;
    }

    public ServiceProvider Services { get; }

    public ManualClock Clock { get; }

    public IReadOnlyCollection<string> Logs => _logs.Messages;

    public Initiator? Initiator
    {
        get => _initiator.Current;
        set => _initiator.Current = value;
    }

    public static async Task<WebhookScenario> CreateAsync(string provider)
    {
        var database = await TemporaryDatabase.CreateAsync(provider);
        var initiator = new SwitchableInitiator { Current = Amir };
        var clock = new ManualClock(Start);
        var logs = new CapturedLogs();
        var settings = new Dictionary<string, string?>(database.Configuration)
        {
            ["Outbound:AllowedHosts:0"] = "example.com",
            ["Outbound:AllowedHosts:1"] = "localhost:5005",
        };
        var services = TestServices.Build(
            settings,
            collection => collection
                .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
                .AddSingleton<ICurrentInitiator>(initiator)
                .AddSingleton<TimeProvider>(clock)
                .AddSingleton<ISecretProtector, EphemeralSecretProtector>());

        await TestServices.MigrateAsync(services);
        return new WebhookScenario(database, services, initiator, clock, logs);
    }

    public static NewWebhookSubscription Frontend(string url = "https://hooks.example.com/fluentcms")
    {
        return new NewWebhookSubscription(url, [WebhookEvents.ChangeSetReverted, WebhookEvents.ChangeSetPublished]);
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<T>(Func<IWebhookDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IWebhookDirectory>());
    }

    public Task<List<WebhookSubscriptionRow>> RowsAsync()
    {
        return QueryAsync(context => context.WebhookSubscriptions.AsNoTracking().ToListAsync());
    }

    public Task<List<AuditEventRow>> EventsAsync()
    {
        return QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.SubjectType == "webhook_subscription").ToListAsync());
    }

    public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    public string Unprotect(string protectedSecret)
    {
        return Services.GetRequiredService<ISecretProtector>().Unprotect(WebhookSecrets.ProtectionPurpose, protectedSecret);
    }

    public Task<bool> StoresTextAsync(string text)
    {
        return _database.StoresTextAsync(Services, text);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _database.DisposeAsync();
    }

    private sealed class SwitchableInitiator : ICurrentInitiator
    {
        public Initiator? Current { get; set; }
    }

    // The host's protector on a key ring that lives as long as the test, so what is stored is real ciphertext.
    private sealed class EphemeralSecretProtector : ISecretProtector
    {
        private readonly EphemeralDataProtectionProvider _provider = new();

        public string Protect(string purpose, string secret)
        {
            return _provider.CreateProtector(purpose).Protect(secret);
        }

        public string Unprotect(string purpose, string protectedSecret)
        {
            return _provider.CreateProtector(purpose).Unprotect(protectedSecret);
        }
    }
}
