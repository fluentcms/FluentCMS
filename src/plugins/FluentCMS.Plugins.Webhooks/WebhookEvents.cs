using FluentCMS.Authorization;
using FluentCMS.Events;

namespace FluentCMS.Plugins.Webhooks;

internal sealed class WebhookPermissions : IPermissionProvider
{
    public const string Manage = "webhooks.manage";

    public IEnumerable<Permission> GetPermissions() => [new Permission(Manage, "Create, change and remove the webhooks of a site, and read and resend their deliveries.")];
}

// The events a webhook subscribes to, by the names of their activity rows.
internal static class WebhookEvents
{
    public const string Ping = "ping";
    public const string CacheInvalidated = "cache.invalidated";

    public static readonly IReadOnlyDictionary<Type, string> Names = new Dictionary<Type, string>
    {
        [typeof(EntryDraftSavedEvent)] = "content.draft_saved",
        [typeof(EntryPublishedEvent)] = "content.published",
        [typeof(EntryUnpublishedEvent)] = "content.unpublished",
        [typeof(EntryDraftDiscardedEvent)] = "content.draft_discarded",
        [typeof(EntryVersionRestoredEvent)] = "content.version_restored",
        [typeof(EntryTrashedEvent)] = "content.trashed",
        [typeof(EntryRestoredEvent)] = "content.restored",
        [typeof(EntryPurgedEvent)] = "content.purged",
        [typeof(EntryScheduledEvent)] = "content.scheduled",
        [typeof(EntryScheduleCancelledEvent)] = "content.schedule_cancelled",
        [typeof(CacheInvalidatedEvent)] = CacheInvalidated,
    };

    public static readonly IReadOnlySet<string> Subscribable = Names.Values.ToHashSet(StringComparer.Ordinal);

    // What the event leaves the version as.
    public static string StatusOf(EntryEvent message) => message switch
    {
        EntryPublishedEvent or EntryDraftDiscardedEvent => "published",
        EntryUnpublishedEvent => "unpublished",
        EntryTrashedEvent => "trashed",
        EntryPurgedEvent => "purged",
        EntryScheduledEvent => "scheduled",
        EntryScheduleCancelledEvent => "unscheduled",
        _ => "draft",
    };
}

// The activity rows of a webhook's changes. They carry no secret, URL or header.
public sealed record WebhookCreatedEvent(Guid WebhookId, string Name) : CmsEvent;

public sealed record WebhookUpdatedEvent(Guid WebhookId, string Name, bool SecretChanged) : CmsEvent;

public sealed record WebhookDeletedEvent(Guid WebhookId, string Name) : CmsEvent;

// A delivery whose last attempt failed, so that nothing more is sent of it. Event is the name of the event it carried.
// A ping or a redelivery, which is tried once, raises none.
public sealed record WebhookDeadEvent(Guid WebhookId, string Name, Guid DeliveryId, string Event, int Attempts) : CmsEvent;

// A webhook that WebhooksOptions.DisableAfter turned off, because every attempt to it since FailingSince failed.
public sealed record WebhookDisabledEvent(Guid WebhookId, string Name, DateTimeOffset FailingSince) : CmsEvent;
