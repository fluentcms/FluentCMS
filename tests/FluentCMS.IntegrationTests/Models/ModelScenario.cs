using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Models;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Models;

// A migrated database on one provider with the real provider wiring, whose HTTP requests reach an in-process fake of each provider's
// API instead of the network. Three profiles: OpenAI, a local OpenAI-compatible server and Anthropic.
internal sealed class ModelScenario : IAsyncDisposable
{
    public static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    public static readonly ModelRequest Tone = new(
        "openai",
        "check:tone",
        "Decide whether the content keeps a neutral tone.",
        [new ModelData("content", "The best kettle ever made.")]);

    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;

    private ModelScenario(TemporaryDatabase database, ServiceProvider services, SwitchableInitiator initiator, ManualClock clock, FakeProviders providers)
    {
        _database = database;
        Services = services;
        _initiator = initiator;
        Clock = clock;
        Providers = providers;
    }

    public ServiceProvider Services { get; }

    public ManualClock Clock { get; }

    public FakeProviders Providers { get; }

    public Initiator? Initiator
    {
        get => _initiator.Current;
        set => _initiator.Current = value;
    }

    public static async Task<ModelScenario> CreateAsync(string provider, Dictionary<string, string?>? settings = null)
    {
        var database = await TemporaryDatabase.CreateAsync(provider);
        var initiator = new SwitchableInitiator { Current = Amir };
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
        var providers = new FakeProviders();
        var configuration = new Dictionary<string, string?>(database.Configuration)
        {
            ["Models:Profiles:openai:Provider"] = "openai",
            ["Models:Profiles:openai:Model"] = "gpt-fake",
            ["Models:Profiles:openai:ApiKey"] = "sk-test-openai",
            ["Models:Profiles:openai:InputCostPerMillionTokens"] = "10",
            ["Models:Profiles:openai:OutputCostPerMillionTokens"] = "20",
            ["Models:Profiles:local:Provider"] = "openai",
            ["Models:Profiles:local:Endpoint"] = "http://localhost:11434/v1",
            ["Models:Profiles:local:Model"] = "llama-fake",
            ["Models:Profiles:anthropic:Provider"] = "anthropic",
            ["Models:Profiles:anthropic:Model"] = "claude-fake",
            ["Models:Profiles:anthropic:ApiKey"] = "sk-test-anthropic",
            ["Models:Profiles:anthropic:InputCostPerMillionTokens"] = "3",
            ["Models:Profiles:anthropic:OutputCostPerMillionTokens"] = "15",
            ["Models:Profiles:broken:Provider"] = "openai",
            ["Models:Profiles:broken:Model"] = "gpt-broken",
            ["Models:Profiles:broken:InputCostPerMillionTokens"] = "10",
            ["Models:Profiles:broken:OutputCostPerMillionTokens"] = "20",
            ["Outbound:AllowedHosts:0"] = "api.openai.com",
            ["Outbound:AllowedHosts:1"] = "localhost",
            ["Outbound:AllowedHosts:2"] = "anthropic.com",
        };
        foreach (var (key, value) in settings ?? [])
        {
            configuration[key] = value;
        }

        var services = TestServices.Build(
            configuration,
            collection => collection
                .AddSingleton<ICurrentInitiator>(initiator)
                .AddSingleton<TimeProvider>(clock)
                .AddSingleton(new ModelTransport(() => providers)));

        await TestServices.MigrateAsync(services);
        return new ModelScenario(database, services, initiator, clock, providers);
    }

    // Each call gets its own scope, as separate requests would.
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<FluentCMS.Core.Results.Result<ModelAnswer>> AskAsync(ModelRequest request)
    {
        return RunAsync(services => services.GetRequiredService<IModelGateway>().AskAsync(request));
    }

    public async Task<AgentCredential> IssueAgentAsync(decimal spendCap)
    {
        var current = Initiator;
        Initiator = Amir;
        var issued = await RunAsync(services => services.GetRequiredService<IAgentCredentialDirectory>().IssueAsync(new NewAgentCredential(
            "tone-checker",
            Clock.Now.AddDays(90),
            AgentScope.Create([Site.Default.Id], null, null, [AgentOperation.Read]).Value,
            AgentCaps.Create(600, TimeSpan.FromHours(1), spendCap, TimeSpan.FromDays(1), 200, 20).Value)));
        Initiator = current;
        return issued.Value.Credential;
    }

    public Task<List<ModelCallRow>> CallRowsAsync()
    {
        return QueryAsync(context => context.ModelCalls.AsNoTracking().OrderBy(call => call.Id).ToListAsync());
    }

    public Task<List<AuditEventRow>> PurgeEventsAsync()
    {
        return QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.Operation == "model_call.purged").OrderBy(row => row.Id).ToListAsync());
    }

    public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
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

// Answers the OpenAI chat completions API and the Anthropic messages API with fixed answers, and keeps every request it saw.
internal sealed class FakeProviders : HttpMessageHandler
{
    public ConcurrentQueue<(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body)> Requests { get; } = new();

    // Set to hold every answer until it completes, as a slow provider would.
    public TaskCompletionSource? Gate { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(",", header.Value));
        Requests.Enqueue((request.Method, request.RequestUri!, headers, body));
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        var path = request.RequestUri!.AbsolutePath;
        if (body.Contains("gpt-broken", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"error": {"message": "Upstream failure, request id secret-123."}}""", Encoding.UTF8, "application/json"),
            };
        }

        var json = path.EndsWith("/chat/completions", StringComparison.Ordinal)
            ? """
                {"id": "chatcmpl-1", "object": "chat.completion", "created": 1760000000, "model": "MODEL",
                 "choices": [{"index": 0, "message": {"role": "assistant", "content": "{\"verdict\": \"pass\"}"}, "finish_reason": "stop"}],
                 "usage": {"prompt_tokens": 1000, "completion_tokens": 100, "total_tokens": 1100}}
                """.Replace("MODEL", request.RequestUri.Host == "localhost" ? "llama-fake:8b" : "gpt-fake-2026-01-01", StringComparison.Ordinal)
            : path.EndsWith("/v1/messages", StringComparison.Ordinal)
                ? """
                    {"id": "msg_1", "type": "message", "role": "assistant", "model": "claude-fake-20260101",
                     "content": [{"type": "text", "text": "{\"verdict\": \"fail\"}"}], "stop_reason": "end_turn", "stop_sequence": null,
                     "usage": {"input_tokens": 2000, "output_tokens": 200}}
                    """
                : null;

        return json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
