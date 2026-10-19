using FluentCMS.Core;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Identity;

public sealed class SystemInitiatorTests
{
    [Fact]
    public void A_component_has_one_stable_id_and_the_audit_log_names_it_system()
    {
        Assert.Equal(SystemInitiator.Checks.Id, new SystemInitiator("checks").Id);
        Assert.NotEqual(SystemInitiator.Checks.Id, SystemInitiator.Retention.Id);
        Assert.Equal(("checks", "checks"), (SystemInitiator.Checks.Component, SystemInitiator.Checks.DisplayName));
        Assert.Equal(("system", SystemInitiator.Retention.Id), InitiatorKinds.Describe(SystemInitiator.Retention));
        Assert.Throws<ArgumentException>(() => new SystemInitiator("Check Engine"));
    }

    // CHG-01: a change set always has a person or an agent behind it.
    [Fact]
    public void A_change_set_cannot_be_opened_by_a_part_of_the_product()
    {
        var opened = ChangeSet.Open(Site.Default.Id, "Tidy up.", [], SystemInitiator.Checks);

        var error = Assert.Single(opened.Errors);
        Assert.Equal(ChangeSetErrorCodes.InitiatorNotAllowed, error.Code);
        Assert.Equal("A change set is opened by a person or an agent, and 'checks' is part of the product.", error.Message);
    }

    [Fact]
    public void A_published_set_cannot_be_reverted_by_a_part_of_the_product()
    {
        var published = Set(ChangeSetState.Published, new UserInitiator(Guid.NewGuid(), "Amir"));

        Assert.Equal(ChangeSetErrorCodes.InitiatorNotAllowed, Assert.Single(published.Revert("Undo.", SystemInitiator.Checks).Errors).Code);
    }

    internal static ChangeSet Set(ChangeSetState state, Initiator initiator)
    {
        var operation = ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, Guid.NewGuid(), null, 1).Value;
        return ChangeSet.Restore(Guid.NewGuid(), Site.Default.Id, "Launch the kettle.", [], initiator, null, [operation], state, null);
    }
}

// ADR-0012: the check engine makes the moves it owns and reads content; everything else is refused, and agents still cannot make
// the engine's moves.
public sealed class SystemPermissionTests : IDisposable
{
    private static readonly AgentInitiator Agent = new(Guid.NewGuid(), "example-agent");

    private readonly SwitchableInitiator _initiator = new();
    private readonly RecordingAuditLog _audit = new();
    private readonly ServiceProvider _provider;

    public SystemPermissionTests()
    {
        var caps = AgentCaps.Create(100, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), 50, 10).Value;
        var credential = AgentCredential.Restore(
            Agent.CredentialId,
            Agent.DisplayName,
            AgentScope.Restore([Site.Default.Id], null, null, [AgentOperation.Read, AgentOperation.Create, AgentOperation.Update]),
            caps,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            null);

        _provider = new ServiceCollection()
            .AddFluentCmsCore()
            .AddSingleton<ICurrentInitiator>(_initiator)
            .AddSingleton<IAgentCredentialDirectory>(new OneCredential(credential))
            .AddSingleton<IAgentActivity>(new NoActivity())
            .AddSingleton<IUnitOfWork>(new NoUnitOfWork())
            .AddSingleton<IAuditLog>(_audit)
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
    }

    private IAgentPermissions Permissions => _provider.GetRequiredService<IAgentPermissions>();

    [Theory]
    [InlineData(ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed, true)]
    [InlineData(ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview, true)]
    [InlineData(ChangeSetState.ChecksRunning, ChangeSetState.Approved, false)]
    [InlineData(ChangeSetState.Open, ChangeSetState.ChecksRunning, false)]
    [InlineData(ChangeSetState.ChecksFailed, ChangeSetState.Open, false)]
    [InlineData(ChangeSetState.AwaitingReview, ChangeSetState.Approved, false)]
    [InlineData(ChangeSetState.Approved, ChangeSetState.Published, false)]
    [InlineData(ChangeSetState.ChecksFailed, ChangeSetState.Rejected, false)]
    [InlineData(ChangeSetState.Published, ChangeSetState.Reverted, false)]
    public async Task The_check_engine_makes_only_the_moves_it_owns(ChangeSetState from, ChangeSetState to, bool allowed)
    {
        _initiator.Current = SystemInitiator.Checks;

        var result = await Permissions.CheckTransitionAsync(SystemInitiatorTests.Set(from, Agent), to);

        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed)
        {
            Assert.Equal(AgentPermissionErrorCodes.SystemNotPermitted, Assert.Single(result.Errors).Code);
        }
    }

    [Fact]
    public async Task Another_engine_owns_none_of_the_checks_moves()
    {
        _initiator.Current = SystemInitiator.Retention;

        var set = SystemInitiatorTests.Set(ChangeSetState.ChecksRunning, Agent);

        var result = await Permissions.CheckTransitionAsync(set, ChangeSetState.AwaitingReview);

        Assert.Equal(
            $"The 'retention' engine cannot move change set {set.Id} from checks running to awaiting review: "
            + "it makes only the moves it owns and never writes content (ADR-0012).",
            Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task An_engine_reads_content_and_never_writes_it_opens_or_adds_to_a_set_or_calls_a_tool()
    {
        _initiator.Current = SystemInitiator.Checks;
        var set = SystemInitiatorTests.Set(ChangeSetState.Open, Agent);

        Assert.True((await Permissions.CheckContentAsync("product-page", AgentOperation.Read)).IsSuccess);
        Assert.True((await Permissions.CheckDefinitionAsync("product-page", AgentOperation.Read)).IsSuccess);
        Assert.True((await Permissions.ReadableTypesAsync())("product-page"));

        Result[] refused =
        [
            await Permissions.CheckContentAsync("product-page", AgentOperation.Create),
            await Permissions.CheckContentAsync("product-page", AgentOperation.Update, ["headline"]),
            await Permissions.CheckContentAsync("product-page", AgentOperation.Delete),
            await Permissions.CheckDefinitionAsync("product-page", AgentOperation.Update),
            await Permissions.CheckNewChangeSetAsync(),
            await Permissions.CheckChangeSetSizeAsync(set, 1),
            await Permissions.CheckToolCallAsync(),
        ];

        Assert.All(refused, result => Assert.Equal(AgentPermissionErrorCodes.SystemNotPermitted, Assert.Single(result.Errors).Code));
        Assert.Empty(_audit.Events);
    }

    // The engine's moves stay out of an agent's reach: a set it submitted is moved on by the checks, not by the agent.
    [Theory]
    [InlineData(ChangeSetState.ChecksFailed)]
    [InlineData(ChangeSetState.AwaitingReview)]
    public async Task An_agent_still_cannot_make_the_checks_moves_on_its_own_set(ChangeSetState target)
    {
        _initiator.Current = Agent;

        var result = await Permissions.CheckTransitionAsync(SystemInitiatorTests.Set(ChangeSetState.ChecksRunning, Agent), target);

        Assert.Equal(AgentPermissionErrorCodes.TransitionNotPermitted, Assert.Single(result.Errors).Code);
        Assert.Equal("agent.transition_refused", Assert.Single(_audit.Events).Operation);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private sealed class SwitchableInitiator : ICurrentInitiator
    {
        public Initiator? Current { get; set; }
    }

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<NewAuditEvent> Events { get; } = [];

        public Task<AuditEvent> AppendAsync(NewAuditEvent entry, CancellationToken cancellationToken = default)
        {
            Events.Add(entry);
            return Task.FromResult(new AuditEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Initiator = Agent,
                Operation = entry.Operation,
            });
        }
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkScope>(new Scope());
        }

        private sealed class Scope : IUnitOfWorkScope
        {
            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class NoActivity : IAgentActivity
    {
        public Task<int> ChangeSetsOpenedSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task<int> ToolCallsSinceAsync(Guid credentialId, DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class OneCredential(AgentCredential credential) : IAgentCredentialDirectory
    {
        public Task<IReadOnlyList<AgentCredential>> ListAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AgentCredential>>([credential]);
        }

        public Task<Result<AgentCredential>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(id == credential.Id
                ? Result<AgentCredential>.Success(credential)
                : Result<AgentCredential>.Failure("credential_not_found", "No such credential."));
        }

        public Task<Result<IssuedAgentCredential>> IssueAsync(NewAgentCredential issued, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AgentCredential>> RevokeAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<AgentCredential>> AuthenticateAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
