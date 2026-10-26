namespace FluentCMS.Admin;

// Sizes the host and the admin's components must agree on.
public static class AdminLimits
{
    // The largest message the admin's circuit hub takes, so a component's prerendered state, carried in the circuit's first message,
    // fits.
    public const long CircuitStartBytes = 1024 * 1024;

    // The most a component keeps, measured as its JSON. Blazor encodes kept state twice and encrypts it on the way, so it reaches the
    // hub about 1.8 times as large: 480 KB of JSON stays under the hub's limit with room for the page's other state.
    public const int KeptStateBytes = 480 * 1024;
}
