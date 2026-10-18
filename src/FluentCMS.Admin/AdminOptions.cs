namespace FluentCMS.Admin;

public sealed class AdminOptions
{
    // The /ui-spike showcase is reachable only where the host enables it (Development), and like every admin page only when signed in.
    public bool ShowcaseEnabled { get; set; }
}
