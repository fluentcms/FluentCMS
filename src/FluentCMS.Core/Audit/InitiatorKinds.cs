using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Audit;

// How the log names an initiator, in the table and in every export.
public static class InitiatorKinds
{
    public const string User = "user";
    public const string Agent = "agent";

    public static (string Kind, Guid Id) Describe(Initiator initiator)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        return initiator switch
        {
            UserInitiator user => (User, user.UserId),
            AgentInitiator agent => (Agent, agent.CredentialId),
            _ => throw new NotSupportedException($"The audit log cannot store an initiator of type {initiator.GetType().Name}."),
        };
    }
}
