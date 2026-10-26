using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;

namespace FluentCMS.Core.Agents;

internal sealed class AgentPermissions(
    ICurrentInitiator currentInitiator,
    IAgentCredentialDirectory credentials,
    IAgentActivity activity,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider clock) : IAgentPermissions
{
    // Moves the checks, routing and people make. Listing what an agent may do, rather than what it may not, keeps a state
    // added to the table out of an agent's reach until someone decides otherwise.
    private static readonly ChangeSetState[] AgentTargets = [ChangeSetState.ChecksRunning, ChangeSetState.Open];

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    // The moves each part of the product owns (ADR-0012); routing adds auto-publish when it lands. Anything else an engine tries is
    // refused, so a new engine moves nothing until someone writes down what it owns.
    private static readonly Dictionary<string, (ChangeSetState From, ChangeSetState To)[]> SystemMoves = new(StringComparer.Ordinal)
    {
        [SystemInitiator.Checks.Component] =
        [
            (ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed),
            (ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview),
        ],
    };

    public async Task<Result> CheckTransitionAsync(ChangeSet changeSet, ChangeSetState target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        if (currentInitiator.Current is SystemInitiator system)
        {
            return SystemMoves.GetValueOrDefault(system.Component, []).Contains((changeSet.State, target))
                ? Result.Success()
                : SystemRefused(system, $"move change set {changeSet.Id} from {Describe(changeSet.State)} to {Describe(target)}");
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential)
        {
            return Result.Success();
        }

        var changeSetId = changeSet.Id;
        if (AgentTargets.Contains(target))
        {
            return Owns(credential, changeSet);
        }

        await RecordAsync(
            new NewAuditEvent("agent.transition_refused") { SubjectType = "change_set", SubjectId = changeSetId, ChangeSetId = changeSetId },
            cancellationToken);

        return Result.Failure(
            AgentPermissionErrorCodes.TransitionNotPermitted,
            $"The credential '{credential.DisplayName}' cannot move change set {changeSetId} to {Describe(target)}: {Owner(target)}. "
            + "An agent submits its change set for checks and reopens it after failed checks.");
    }

    public async Task<Result> CheckNewChangeSetAsync(CancellationToken cancellationToken = default)
    {
        if (currentInitiator.Current is SystemInitiator system)
        {
            return SystemRefused(system, "open a change set");
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential)
        {
            return Result.Success();
        }

        // Called inside the unit of work that opens the set, so concurrent opens by one agent are counted one at a time.
        await activity.HoldAsync(credential.Id, cancellationToken);

        var cap = credential.Caps.ChangeSetsPerDay;
        var opened = await activity.ChangeSetsOpenedSinceAsync(credential.Id, clock.GetUtcNow() - Day, cancellationToken);
        if (opened < cap)
        {
            return Result.Success();
        }

        await RecordAsync(
            new NewAuditEvent("agent.change_sets_per_day_exceeded") { SubjectType = "agent_credential", SubjectId = credential.Id },
            cancellationToken);

        return Result.Failure(
            AgentPermissionErrorCodes.CapExceeded,
            $"The credential '{credential.DisplayName}' has opened {opened} change sets in the last 24 hours, and its cap is {cap} "
            + $"(change sets per day). Add the operation to a change set that is still open, or wait until fewer than {cap} were opened in the last 24 hours.");
    }

    public async Task<Result> CheckChangeSetReadAsync(ChangeSet changeSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        if (currentInitiator.Current is SystemInitiator)
        {
            return Result.Success();
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        return agent.Value is { } credential && !(changeSet.Initiator is AgentInitiator owner && owner.CredentialId == credential.Id)
            ? Result.Failure(
                AgentPermissionErrorCodes.NotOwner,
                $"Change set {changeSet.Id} is not one the credential '{credential.DisplayName}' opened; an agent reads its own change sets only.")
            : Result.Success();
    }

    public async Task<Result> CheckChangeSetSizeAsync(ChangeSet changeSet, int adding = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        if (currentInitiator.Current is SystemInitiator system)
        {
            return SystemRefused(system, $"add to change set {changeSet.Id}");
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential)
        {
            return Result.Success();
        }

        var owned = Owns(credential, changeSet);
        if (!owned.IsSuccess)
        {
            return owned;
        }

        var cap = credential.Caps.ItemsPerChangeSet;
        var count = changeSet.Operations.Count + adding;
        if (count <= cap)
        {
            return Result.Success();
        }

        await RecordAsync(
            new NewAuditEvent("agent.items_per_change_set_exceeded") { SubjectType = "change_set", SubjectId = changeSet.Id, ChangeSetId = changeSet.Id },
            cancellationToken);

        return Result.Failure(
            AgentPermissionErrorCodes.CapExceeded,
            $"Change set {changeSet.Id} already holds {count - 1} items, and the credential '{credential.DisplayName}' allows "
            + $"{cap} (items per change set). Submit this change set and add the rest to a new one.");
    }

    public async Task<Result> CheckNewChangeSetSizeAsync(int items, CancellationToken cancellationToken = default)
    {
        if (currentInitiator.Current is SystemInitiator system)
        {
            return SystemRefused(system, "open a change set");
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential || items <= credential.Caps.ItemsPerChangeSet)
        {
            return Result.Success();
        }

        await RecordAsync(
            new NewAuditEvent("agent.items_per_change_set_exceeded") { SubjectType = "agent_credential", SubjectId = credential.Id },
            cancellationToken);

        return Result.Failure(
            AgentPermissionErrorCodes.CapExceeded,
            $"{items} items are more than the credential '{credential.DisplayName}' allows in one change set: {credential.Caps.ItemsPerChangeSet} "
            + "(items per change set). Split them over several calls of at most that many.");
    }

    public async Task<Result> CheckContentAsync(
        string contentType,
        AgentOperation operation,
        IReadOnlyCollection<string>? fields = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        // An engine reads what it judges, and never writes content (ADR-0012).
        if (currentInitiator.Current is SystemInitiator system)
        {
            return operation == AgentOperation.Read ? Result.Success() : SystemRefused(system, $"{Verb(operation)} entries of '{contentType}'");
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential)
        {
            return Result.Success();
        }

        // Reading a type covers its definition and its entries; every other operation is on entries.
        var target = operation == AgentOperation.Read ? $"the content type '{contentType}' or its entries" : $"entries of '{contentType}'";
        var allowed = Allows(credential, operation, contentType, target);
        if (!allowed.IsSuccess || fields is null)
        {
            return allowed;
        }

        var outside = fields
            .Where(field => !credential.Scope.AllowsField(contentType, field))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return outside.Count == 0
            ? Result.Success()
            : Result.Failure(
                AgentPermissionErrorCodes.ScopeDenied,
                $"The credential '{credential.DisplayName}' may not change {Fields(outside)} of '{contentType}': its scope allows only "
                + $"{Fields(ListedFields(credential.Scope, contentType))}. Leave the other fields as they are, or ask an administrator for a "
                + "credential that includes them.");
    }

    public async Task<Result> CheckMediaAsync(AgentOperation operation, CancellationToken cancellationToken = default)
    {
        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential || credential.Scope.AllowsOperation(operation))
        {
            return Result.Success();
        }

        return Result.Failure(
            AgentPermissionErrorCodes.ScopeDenied,
            $"The credential '{credential.DisplayName}' may not {Verb(operation)} media: its scope allows "
            + $"{string.Join(", ", credential.Scope.Operations.Select(Verb))} only. Ask an administrator for a credential that allows {Verb(operation)}.");
    }

    public async Task<Result> CheckDefinitionAsync(string contentType, AgentOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentType);

        if (currentInitiator.Current is SystemInitiator system)
        {
            return operation == AgentOperation.Read ? Result.Success() : SystemRefused(system, $"{Verb(operation)} the content type '{contentType}'");
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential)
        {
            return Result.Success();
        }

        var allowed = Allows(credential, operation, contentType, $"the content type '{contentType}'");
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var listed = ListedFields(credential.Scope, contentType);
        return listed.Count == 0
            ? Result.Success()
            : Result.Failure(
                AgentPermissionErrorCodes.ScopeDenied,
                $"The credential '{credential.DisplayName}' may not {Verb(operation)} the definition of '{contentType}': its scope limits "
                + $"'{contentType}' to {Fields(listed)}, and a definition covers every field.");
    }

    public async Task<Func<string, bool>> ReadableTypesAsync(CancellationToken cancellationToken = default)
    {
        if (currentInitiator.Current is SystemInitiator)
        {
            return _ => true;
        }

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return _ => false;
        }

        if (agent.Value is not { } credential)
        {
            return _ => true;
        }

        var scope = credential.Scope;
        return scope.AllowsOperation(AgentOperation.Read) ? scope.AllowsType : _ => false;
    }

    public async Task<Result> ClaimToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (currentInitiator.Current is SystemInitiator system)
        {
            return SystemRefused(system, "call a tool");
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            await activity.StartToolCallAsync(call with { Outcome = ToolCallOutcome.Refused }, cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential)
        {
            await scope.CommitAsync(cancellationToken);
            return Result.Success();
        }

        // One credential's calls take turns from here to the commit, so each count sees every call admitted before it.
        await activity.HoldAsync(credential.Id, cancellationToken);

        var caps = credential.Caps;
        var made = await activity.ToolCallsSinceAsync(credential.Id, clock.GetUtcNow() - caps.CallPeriod, cancellationToken);
        if (made < caps.CallsPerPeriod)
        {
            await activity.StartToolCallAsync(call with { Outcome = ToolCallOutcome.Running }, cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return Result.Success();
        }

        await activity.StartToolCallAsync(call with { Outcome = ToolCallOutcome.Refused }, cancellationToken);
        await auditLog.AppendAsync(
            new NewAuditEvent("agent.calls_per_period_exceeded") { SubjectType = "agent_credential", SubjectId = credential.Id },
            cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result.Failure(
            AgentPermissionErrorCodes.CapExceeded,
            $"The credential '{credential.DisplayName}' has made {made} tool calls {Within(caps.CallPeriod)}, and its cap is "
            + $"{caps.CallsPerPeriod} (calls per period). Wait until fewer than {caps.CallsPerPeriod} calls fall {Within(caps.CallPeriod)}, then call again.");
    }

    // A refusal here is a fault in the product, not in what a caller asked, so it is not recorded as a security event.
    private static Result SystemRefused(SystemInitiator system, string action)
    {
        return Result.Failure(
            AgentPermissionErrorCodes.SystemNotPermitted,
            $"The '{system.Component}' engine cannot {action}: it makes only the moves it owns and never writes content (ADR-0012).");
    }

    // The operation first, then the type, so the message names the first part of the scope that refuses.
    private static Result Allows(AgentCredential credential, AgentOperation operation, string contentType, string target)
    {
        var scope = credential.Scope;

        if (!scope.AllowsOperation(operation))
        {
            return Result.Failure(
                AgentPermissionErrorCodes.ScopeDenied,
                $"The credential '{credential.DisplayName}' may not {Verb(operation)} {target}: its scope allows "
                + $"{string.Join(", ", scope.Operations.Select(Verb))} only. Ask an administrator for a credential that allows {Verb(operation)}.");
        }

        if (!scope.AllowsType(contentType))
        {
            return Result.Failure(
                AgentPermissionErrorCodes.ScopeDenied,
                $"The credential '{credential.DisplayName}' may not {Verb(operation)} {target}: '{contentType}' is not among the content types "
                + $"in its scope, which are {string.Join(", ", scope.Types!.Select(type => $"'{type}'"))}.");
        }

        return Result.Success();
    }

    // The fields the scope lists for the type; none when the type keeps all its fields.
    private static List<string> ListedFields(AgentScope scope, string contentType)
    {
        var prefix = contentType + ".";
        return (scope.Fields ?? [])
            .Where(field => field.StartsWith(prefix, StringComparison.Ordinal))
            .Select(field => field[prefix.Length..])
            .ToList();
    }

    private static string Fields(IReadOnlyCollection<string> fields)
    {
        return (fields.Count == 1 ? "the field " : "the fields ") + string.Join(", ", fields.Select(field => $"'{field}'"));
    }

    private static string Verb(AgentOperation operation)
    {
        return operation.ToString().ToLowerInvariant();
    }

    private static string Within(TimeSpan period)
    {
        return period == TimeSpan.FromHours(1) ? "in the last hour"
            : period == TimeSpan.FromDays(1) ? "in the last day"
            : $"in the last {period}";
    }

    // An agent works on the change sets it opened; a person's set, or another agent's, is not its to submit or add to.
    private static Result Owns(AgentCredential credential, ChangeSet changeSet)
    {
        return changeSet.Initiator is AgentInitiator owner && owner.CredentialId == credential.Id
            ? Result.Success()
            : Result.Failure(
                AgentPermissionErrorCodes.NotOwner,
                $"Change set {changeSet.Id} was opened by {changeSet.Initiator.DisplayName}, not by the credential '{credential.DisplayName}'; "
                + "an agent submits, reopens and adds to its own change sets only. Open a change set of your own.");
    }

    // Null for anyone but an agent; the agent's credential while it is active and its scope includes the site.
    private async Task<Result<AgentCredential?>> AgentAsync(CancellationToken cancellationToken)
    {
        if (currentInitiator.Current is not AgentInitiator agent)
        {
            return Result<AgentCredential?>.Success(null);
        }

        var credential = await credentials.GetAsync(agent.CredentialId, cancellationToken);
        if (!credential.IsSuccess)
        {
            return Result<AgentCredential?>.Failure(credential.Errors);
        }

        var active = credential.Value.CheckActive(clock.GetUtcNow());
        if (!active.IsSuccess)
        {
            return Result<AgentCredential?>.Failure(active.Errors);
        }

        // Until multi-site (MST-01), everything belongs to the one site (SCH-07).
        if (!credential.Value.Scope.AllowsSite(Site.Default.Id))
        {
            return Result<AgentCredential?>.Failure(
                AgentPermissionErrorCodes.SiteOutOfScope,
                $"The credential '{credential.Value.DisplayName}' does not include the site '{Site.Default.Name}' in its scope; "
                + "ask an administrator for a credential that does.");
        }

        return Result<AgentCredential?>.Success(credential.Value);
    }

    // The refused write leaves nothing, so the event commits on its own, or with the caller's unit of work when there is one. That
    // unit of work usually rolls back because of the refusal, so the event is appended again once it has, in a unit of work of its own.
    private async Task RecordAsync(NewAuditEvent refusal, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        unitOfWork.AfterRollback(async afterwards =>
        {
            await using var again = await unitOfWork.BeginAsync(afterwards);
            await auditLog.AppendAsync(refusal, afterwards);
            await again.CommitAsync(afterwards);
        });
        await auditLog.AppendAsync(refusal, cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private static string Describe(ChangeSetState state)
    {
        return state switch
        {
            ChangeSetState.ChecksRunning => "checks running",
            ChangeSetState.ChecksFailed => "checks failed",
            ChangeSetState.AwaitingReview => "awaiting review",
            _ => state.ToString().ToLowerInvariant(),
        };
    }

    private static string Owner(ChangeSetState target)
    {
        return target switch
        {
            ChangeSetState.ChecksFailed or ChangeSetState.AwaitingReview => "the checks decide that once they have run",
            ChangeSetState.Approved or ChangeSetState.Rejected => "a person decides that when reviewing the change set",
            ChangeSetState.Published => "publishing follows approval and is never done by an agent",
            ChangeSetState.Reverted => "a person reverts a published change set",
            _ => "that move is not one an agent makes",
        };
    }
}
