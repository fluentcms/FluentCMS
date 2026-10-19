namespace FluentCMS.Core.Audit;

// The security-relevant operations the desk lists in its own view (SEC-08): agent credentials, caps and refused moves, people's
// accounts, and routing rules. Each is appended by the lane that owns it; an operation no code appends yet simply matches nothing.
public static class SecurityEvents
{
    public static IReadOnlyList<string> Operations { get; } =
    [
        "agent_credential.issued",
        "agent_credential.revoked",
        "agent.calls_per_period_exceeded",
        "agent.change_sets_per_day_exceeded",
        "agent.items_per_change_set_exceeded",
        "agent.transition_refused",
        "user.created",
        "user.disabled",
        "user.enabled",
        "user.password_reset_issued",
        "user.password_reset",
        "user.login_linked",
        "routing_rule.created",
        "routing_rule.revised",
        "routing_rule.activated",
        "routing_rule.deactivated",
    ];
}
