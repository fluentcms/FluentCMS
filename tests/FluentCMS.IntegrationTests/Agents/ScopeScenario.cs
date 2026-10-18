using System.Text.Json;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Content;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Agents;

// A migrated database on one provider with two content types and an entry of each, written by a person. A test then acts as an
// agent with the scope it needs.
internal sealed class ScopeScenario : IAsyncDisposable
{
    public static readonly UserInitiator Person = new(Guid.NewGuid(), "Amir");

    public static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;

    private ScopeScenario(TemporaryDatabase database, ServiceProvider services, SwitchableInitiator initiator)
    {
        _database = database;
        Services = services;
        _initiator = initiator;
    }

    public ServiceProvider Services { get; }

    public Initiator? Initiator
    {
        get => _initiator.Current;
        set => _initiator.Current = value;
    }

    public Entry Product { get; private set; } = null!;

    public Entry Category { get; private set; } = null!;

    public static async Task<ScopeScenario> CreateAsync(string provider)
    {
        var database = await TemporaryDatabase.CreateAsync(provider);
        var initiator = new SwitchableInitiator { Current = Person };
        var services = TestServices.Build(
            database.Configuration,
            collection => collection
                .AddSingleton<ICurrentInitiator>(initiator)
                .AddSingleton<TimeProvider>(new ManualClock(Start)));
        await TestServices.MigrateAsync(services);

        var scenario = new ScopeScenario(database, services, initiator);
        await scenario.RunAsync<IContentTypeDirectory, ContentType>(async directory => (await directory.CreateAsync(new NewContentType(
            "Product",
            "product",
            new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("price", "number")])))).Value);
        await scenario.RunAsync<IContentTypeDirectory, ContentType>(async directory => (await directory.CreateAsync(new NewContentType(
            "Category",
            "category",
            new ContentTypeDefinition([new FieldDefinition("name", "text")])))).Value);

        scenario.Product = await scenario.RunAsync<IEntryDirectory, Entry>(async directory =>
            (await directory.CreateAsync(new NewEntry("product", Json("""{"name":"Kettle","price":20}""")))).Value);
        scenario.Category = await scenario.RunAsync<IEntryDirectory, Entry>(async directory =>
            (await directory.CreateAsync(new NewEntry("category", Json("""{"name":"Kitchen"}""")))).Value);

        return scenario;
    }

    // Issued by the person, then every later call runs as the agent until a test sets the initiator again.
    public async Task<AgentCredential> ActAsAgentAsync(IEnumerable<string>? types, IEnumerable<string>? fields, params AgentOperation[] operations)
    {
        Initiator = Person;
        var credential = await RunAsync<IAgentCredentialDirectory, AgentCredential>(async directory => (await directory.IssueAsync(new NewAgentCredential(
            "scoped-agent",
            Start.AddDays(30),
            AgentScope.Create([Site.Default.Id], types, fields, operations).Value,
            AgentCaps.Create(600, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), 200, 20).Value))).Value.Credential);

        Initiator = credential.Initiator;
        return credential;
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    // What a refused write must leave as it was: entries, their versions and the audit log.
    public async Task<(int Entries, int Versions, int Events)> CountsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();

        return (await context.Entries.CountAsync(), await context.EntryVersions.CountAsync(), await context.AuditEvents.CountAsync());
    }

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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
