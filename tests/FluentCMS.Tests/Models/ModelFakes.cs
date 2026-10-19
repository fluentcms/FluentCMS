using FluentCMS.Core;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Models;

// The gateway in Core with fakes for what Infrastructure provides: no provider, no key and no network.
internal sealed class ModelScenario : IDisposable
{
    public static readonly ModelProfile Checks = new("checks", ModelProviders.OpenAi, new Uri("https://api.openai.com/v1"), "gpt-test")
    {
        InputCostPerMillionTokens = 2m,
        OutputCostPerMillionTokens = 8m,
        MaxOutputTokens = 256,
    };

    private readonly ServiceProvider _provider;

    public ModelScenario(params ModelProfile[] profiles)
    {
        Profiles = profiles.Length == 0 ? [Checks] : profiles;
        _provider = new ServiceCollection()
            .AddFluentCmsCore()
            .AddSingleton<IModelProfiles>(_ => new FakeProfiles(Profiles))
            .AddSingleton<IModelClients>(_ => new FakeClients(Client))
            .AddSingleton<IModelCallLog>(Log)
            .AddSingleton<IOutboundHosts>(_ => new FakeOutbound(AllowedHosts))
            .AddSingleton<IAgentCredentialDirectory>(_ => new FakeAgents(Agents))
            .AddSingleton<ICurrentInitiator>(_ => new FakeInitiator(() => Initiator))
            .AddSingleton<TimeProvider>(Clock)
            .BuildServiceProvider();
    }

    public IReadOnlyList<ModelProfile> Profiles { get; }

    public FakeChatClient Client { get; } = new();

    public MemoryCallLog Log { get; } = new();

    public List<string> AllowedHosts { get; } = ["api.openai.com"];

    public Dictionary<Guid, AgentCredential> Agents { get; } = [];

    public Initiator? Initiator { get; set; } = new UserInitiator(Guid.NewGuid(), "Amir");

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));

    public IModelGateway Gateway => _provider.GetRequiredService<IModelGateway>();

    public AgentCredential AddAgent(decimal spendCap, TimeSpan period)
    {
        var caps = AgentCaps.Create(100, TimeSpan.FromHours(1), spendCap, period, 50, 10).Value;
        var agent = AgentCredential.Restore(Guid.NewGuid(), "example-agent", AgentScope.Restore([Guid.NewGuid()], null, null, []), caps,
            Clock.Now.AddDays(-1), Clock.Now.AddDays(30), null);
        Agents[agent.Id] = agent;
        return agent;
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}

internal sealed class FakeChatClient : IChatClient
{
    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public Func<ChatResponse> Answer { get; set; } = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"verdict": "pass"}"""))
    {
        ModelId = "gpt-test-2026-01-01",
        Usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 500 },
    };

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((messages.ToList(), options));
        return Task.FromResult(Answer());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}

internal sealed class MemoryCallLog : IModelCallLog
{
    public List<ModelCall> Calls { get; } = [];

    public Task AppendAsync(ModelCall call, CancellationToken cancellationToken = default)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }

    public Task<decimal> SpendAsync(Guid agentCredentialId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Calls.Where(call => call.AgentCredentialId == agentCredentialId && call.Timestamp >= since).Sum(call => call.Cost));
    }

    public Task<IReadOnlyList<ModelCall>> FindByPromptHashAsync(string promptHash, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ModelCall>>(Calls.Where(call => call.PromptHash == promptHash).ToList());
    }

    public Task<Result<int>> PurgeAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}

file sealed class FakeProfiles(IReadOnlyList<ModelProfile> profiles) : IModelProfiles
{
    public IReadOnlyList<ModelProfile> All => profiles;

    public ModelProfile? Find(string name)
    {
        return profiles.FirstOrDefault(profile => profile.Name == name);
    }
}

file sealed class FakeClients(IChatClient client) : IModelClients
{
    public IChatClient Create(ModelProfile profile)
    {
        return client;
    }
}

file sealed class FakeOutbound(List<string> hosts) : IOutboundHosts
{
    public bool IsAllowed(string host)
    {
        return hosts.Contains(host);
    }
}

file sealed class FakeInitiator(Func<Initiator?> current) : ICurrentInitiator
{
    public Initiator? Current => current();
}

file sealed class FakeAgents(Dictionary<Guid, AgentCredential> agents) : IAgentCredentialDirectory
{
    public Task<IReadOnlyList<AgentCredential>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AgentCredential>>(agents.Values.ToList());
    }

    public Task<Result<AgentCredential>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(agents.TryGetValue(id, out var agent)
            ? Result<AgentCredential>.Success(agent)
            : Result<AgentCredential>.Failure("credential_not_found", "No such credential."));
    }

    public Task<Result<IssuedAgentCredential>> IssueAsync(NewAgentCredential credential, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<AgentCredential>> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<AgentCredential>> AuthenticateAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
