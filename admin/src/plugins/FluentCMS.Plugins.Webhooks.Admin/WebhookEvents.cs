namespace FluentCMS.Plugins.Webhooks.Admin;

// The events a webhook may subscribe to, by group, as the server half fixes them: no answer holds them, and the half is
// released with the server half (spec 1.4, 5.3). Each has a text Webhooks.Event.{name} in the half's resources.
internal static class WebhookEvents
{
    public const string Ping = "ping";

    public static readonly IReadOnlyList<(string Group, IReadOnlyList<string> Names)> Groups =
    [
        ("Content",
        [
            "content.draft_saved",
            "content.published",
            "content.unpublished",
            "content.draft_discarded",
            "content.version_restored",
            "content.scheduled",
            "content.schedule_cancelled",
            "content.trashed",
            "content.restored",
            "content.purged",
        ]),
        ("Media", ["media.uploaded", "media.updated", "media.replaced", "media.moved", "media.deleted"]),
        ("Cache", ["cache.invalidated"]),
    ];

    public static readonly IReadOnlySet<string> All = Groups.SelectMany(group => group.Names).ToHashSet(StringComparer.Ordinal);
}
