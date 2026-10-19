using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Agents;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.IntegrationTests.Agents;

// A migrated database on one provider with the credential directory acting as whoever Initiator names, at the time Clock says.
// Everything logged at any level is captured, so a test can show a key never reaches a log.
internal sealed class AgentCredentialScenario : IAsyncDisposable
{
    public static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    public static readonly AgentInitiator Agent = new(Guid.NewGuid(), "example-agent");

    public static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;
    private readonly CapturedLogs _logs;

    private AgentCredentialScenario(
        TemporaryDatabase database,
        ServiceProvider services,
        SwitchableInitiator initiator,
        ManualClock clock,
        CapturedLogs logs)
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

    public static async Task<AgentCredentialScenario> CreateAsync(string provider)
    {
        var database = await TemporaryDatabase.CreateAsync(provider);
        var initiator = new SwitchableInitiator { Current = Amir };
        var clock = new ManualClock(Start);
        var logs = new CapturedLogs();
        var services = TestServices.Build(
            database.Configuration,
            collection => collection
                .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
                .AddSingleton<ICurrentInitiator>(initiator)
                .AddSingleton<TimeProvider>(clock));

        await TestServices.MigrateAsync(services);
        return new AgentCredentialScenario(database, services, initiator, clock, logs);
    }

    public static NewAgentCredential CatalogSync(DateTimeOffset? expiresAt = null)
    {
        return new NewAgentCredential(
            "catalog-sync",
            expiresAt ?? Start.AddDays(90),
            AgentScope.Create([Site.Default.Id], ["product", "category"], ["product.price"], [AgentOperation.Read, AgentOperation.Update]).Value,
            AgentCaps.Create(600, TimeSpan.FromHours(1), 5.25m, TimeSpan.FromDays(1), 200, 20).Value);
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<T>(Func<IAgentCredentialDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>());
    }

    public async Task<IssuedAgentCredential> IssueAsync(NewAgentCredential? credential = null)
    {
        return (await RunAsync(directory => directory.IssueAsync(credential ?? CatalogSync()))).Value;
    }

    public Task<List<AgentCredentialRow>> RowsAsync()
    {
        return QueryAsync(context => context.AgentCredentials.AsNoTracking().ToListAsync());
    }

    public Task<List<AuditEventRow>> CredentialEventsAsync()
    {
        return QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.SubjectType == "agent_credential").ToListAsync());
    }

    public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
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
}
