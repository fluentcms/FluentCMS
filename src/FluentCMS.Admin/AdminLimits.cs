namespace FluentCMS.Admin;

// Sizes the host and the admin's components must agree on.
public static class AdminLimits
{
    // The largest message the admin's circuit hub takes, so a component's prerendered state, carried in the circuit's first message,
    // fits. A component keeps its state only while it stays well under this.
    public const long CircuitStartBytes = 1024 * 1024;

    public const int KeptStateBytes = 768 * 1024;
}
