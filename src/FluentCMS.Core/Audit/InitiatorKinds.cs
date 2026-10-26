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

    // A stored system initiator: one of the product's engines, with the id its component derives. Any other is refused as a fault,
    // as a stored kind nobody knows is, so reading a row never makes up an engine.
    public static SystemInitiator RestoreSystem(string component, Guid id)
    {
        var system = SystemInitiator.Engines.FirstOrDefault(engine => engine.Component == component)
            ?? throw new InvalidOperationException($"'{component}' is stored as a system component, and the product has no engine of that name.");
        return system.Id == id ? system : throw new InvalidOperationException($"The system component '{component}' is stored with an id that is not its own.");
    }
}
