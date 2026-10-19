using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Identity;

// Who caused a change: a person or an agent, never confusable in code or in the audit log (ADR-0009, AUD-01).
public abstract record Initiator(string DisplayName);

public sealed record UserInitiator(Guid UserId, string DisplayName) : Initiator(DisplayName);

public sealed record AgentInitiator(Guid CredentialId, string DisplayName) : Initiator(DisplayName);

// A part of the product acting on its own, with no person or agent behind it (ADR-0012), named for its component: the check engine
// moving a change set out of checks running, or a retention purge. It never opens a change set (CHG-01), and AgentPermissions lets it
// make only the moves its component owns and read content, never write it.
public sealed record SystemInitiator : Initiator
{
    public SystemInitiator(string component)
        : base(component)
    {
        if (!SchemaNames.IsSlug(component))
        {
            throw new ArgumentException($"'{component}' is not a component name; use lowercase words joined by hyphens, such as 'checks'.", nameof(component));
        }

        Component = component;
        Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("fluentcms:system:" + component)).AsSpan(0, 16));
    }

    // The check engine, which moves a set from checks running to checks failed or awaiting review.
    public static SystemInitiator Checks { get; } = new("checks");

    // Maintenance that clears data past its retention, such as stored prompts (AUD-04).
    public static SystemInitiator Retention { get; } = new("retention");

    public string Component { get; }

    // The same for a component every time, so the audit log names one engine one way in every row.
    public Guid Id { get; }
}
