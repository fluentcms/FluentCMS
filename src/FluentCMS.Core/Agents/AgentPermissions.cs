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

    public async Task<Result> CheckTransitionAsync(ChangeSet changeSet, ChangeSetState target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

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
        var agent = await AgentAsync(cancellationToken);
        if (!agent.IsSuccess)
        {
            return Result.Failure(agent.Errors);
        }

        if (agent.Value is not { } credential)
        {
            return Result.Success();
        }

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

    public async Task<Result> CheckChangeSetSizeAsync(ChangeSet changeSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

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
        if (changeSet.Operations.Count <= cap)
        {
            return Result.Success();
        }

        await RecordAsync(
            new NewAuditEvent("agent.items_per_change_set_exceeded") { SubjectType = "change_set", SubjectId = changeSet.Id, ChangeSetId = changeSet.Id },
            cancellationToken);

        return Result.Failure(
            AgentPermissionErrorCodes.CapExceeded,
            $"Change set {changeSet.Id} already holds {changeSet.Operations.Count - 1} items, and the credential '{credential.DisplayName}' allows "
            + $"{cap} (items per change set). Submit this change set and add the rest to a new one.");
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

    // The refused write leaves nothing, so the event commits on its own, or with the caller's unit of work when there is one.
    private async Task RecordAsync(NewAuditEvent refusal, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        await auditLog.AppendAsync(refusal, cancellationToken);
        await scope.CommitAsync(cancellationToken);
    }

    private static string Describe(ChangeSetState state)
    {
        return state switch
        {
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
