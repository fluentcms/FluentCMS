namespace FluentCMS.Admin;

/// <summary>The live region an announcement goes to, after the values of <c>aria-live</c>.</summary>
public enum AnnouncementPoliteness
{
    /// <summary>Read out when the user pauses: saves, publishes and progress.</summary>
    Polite,

    /// <summary>Read out at once, interrupting: errors.</summary>
    Assertive,
}
