using System.Collections.Concurrent;
using System.Text;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Agents;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
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

    private readonly string _provider;
    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;
    private readonly CapturedLogs _logs;

    private AgentCredentialScenario(
        string provider,
        TemporaryDatabase database,
        ServiceProvider services,
        SwitchableInitiator initiator,
        ManualClock clock,
        CapturedLogs logs)
    {
        _provider = provider;
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
        return new AgentCredentialScenario(provider, database, services, initiator, clock, logs);
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

    // Whether any value stored anywhere holds the text: the bytes of the SQLite file, or every row of every table on PostgreSQL.
    public async Task<bool> StoresTextAsync(string text)
    {
        if (_provider == DatabaseProviders.Sqlite)
        {
            SqliteConnection.ClearAllPools();
            var path = new SqliteConnectionStringBuilder(_database.Configuration["Database:ConnectionString"]).DataSource;
            var bytes = await File.ReadAllBytesAsync(path);
            return bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
        }

        return await QueryAsync(async context =>
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            var tables = new List<string>();
            await using (var list = connection.CreateCommand())
            {
                list.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
                await using var reader = await list.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            foreach (var table in tables)
            {
                await using var search = connection.CreateCommand();
                search.CommandText = $"SELECT EXISTS (SELECT 1 FROM \"{table}\" AS stored WHERE strpos(stored::text, @text) > 0)";
                var parameter = search.CreateParameter();
                parameter.ParameterName = "text";
                parameter.Value = text;
                search.Parameters.Add(parameter);

                if ((bool)(await search.ExecuteScalarAsync())!)
                {
                    return true;
                }
            }

            return false;
        });
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

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(this);
        }

        public void Dispose()
        {
        }

        private sealed class Logger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner._messages.Enqueue(formatter(state, exception) + exception);
            }
        }
    }
}
