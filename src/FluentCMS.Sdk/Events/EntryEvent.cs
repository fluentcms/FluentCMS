namespace FluentCMS.Events;

/// <summary>An event about one locale of one entry.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale the event is about.</param>
/// <param name="Version">The version the event is about.</param>
public abstract record EntryEvent(string Type, Guid EntryId, string Locale, int Version) : CmsEvent;

/// <summary>A draft was created or changed.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale of the draft.</param>
/// <param name="Version">The version the draft reached.</param>
public sealed record EntryDraftSavedEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>A draft was published.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale that was published.</param>
/// <param name="Version">The version that went live.</param>
public sealed record EntryPublishedEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>A published entry was taken offline.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale that was unpublished.</param>
/// <param name="Version">The version that was live.</param>
public sealed record EntryUnpublishedEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>A draft was thrown away in favor of the published version.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale whose draft was discarded.</param>
/// <param name="Version">The version the draft went back to.</param>
public sealed record EntryDraftDiscardedEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>An older version became the draft again.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale that was restored.</param>
/// <param name="Version">The new version, which holds the content of an older one.</param>
public sealed record EntryVersionRestoredEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>An entry was moved to the trash.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale that was trashed.</param>
/// <param name="Version">The version of the draft when it was trashed.</param>
public sealed record EntryTrashedEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>An entry came back from the trash.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale that was restored.</param>
/// <param name="Version">The version of the draft.</param>
public sealed record EntryRestoredEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>An entry was deleted for good.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale that was purged.</param>
/// <param name="Version">The last version it had.</param>
public sealed record EntryPurgedEvent(string Type, Guid EntryId, string Locale, int Version) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>A publish or an unpublish was scheduled.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale that was scheduled.</param>
/// <param name="Version">The version the schedule is for.</param>
/// <param name="At">When it is due.</param>
/// <param name="Action"><c>publish</c> or <c>unpublish</c>.</param>
public sealed record EntryScheduledEvent(string Type, Guid EntryId, string Locale, int Version, DateTimeOffset At, string Action) : EntryEvent(Type, EntryId, Locale, Version);

/// <summary>A schedule was cancelled before it came due.</summary>
/// <param name="Type">The name of the content type.</param>
/// <param name="EntryId">The id of the entry.</param>
/// <param name="Locale">The locale whose schedule was cancelled.</param>
/// <param name="Version">The version the schedule was for.</param>
/// <param name="Action"><c>publish</c> or <c>unpublish</c>.</param>
public sealed record EntryScheduleCancelledEvent(string Type, Guid EntryId, string Locale, int Version, string Action) : EntryEvent(Type, EntryId, Locale, Version);
