using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Audit;

// How the log names an initiator, in the table and in every export.
public static class InitiatorKinds
{
    public const string User = "user";
    public const string Agent = "agent";
    public const string System = "system";

    public static (string Kind, Guid Id) Describe(Initiator initiator)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        return initiator switch
        {
            UserInitiator user => (User, user.UserId),
            AgentInitiator agent => (Agent, agent.CredentialId),
            SystemInitiator system => (System, system.Id),
            _ => throw new NotSupportedException($"The audit log cannot store an initiator of type {initiator.GetType().Name}."),
        };
    }

    // The name stored with the kind and id: a system initiator's component, whatever its display name says.
    public static string NameOf(Initiator initiator)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        return initiator is SystemInitiator system ? system.Component : initiator.DisplayName;
    }

    // A stored system initiator, refused when its id is not the one its component derives, as a stored kind nobody knows is.
    public static SystemInitiator RestoreSystem(string component, Guid id)
    {
        try
        {
            var system = new SystemInitiator(component);
            return system.Id == id ? system : throw new InvalidOperationException($"The system component '{component}' is stored with an id that is not its own.");
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"'{component}' is stored as a system component, and it is not a component name.", exception);
        }
    }
}
