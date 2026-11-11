namespace FluentCMS.Events;

/// <summary>A settings area was changed.</summary>
/// <param name="Area">The name of the settings area.</param>
public sealed record SettingsChangedEvent(string Area) : CmsEvent;

/// <summary>A site was created.</summary>
/// <param name="SiteId">The id of the site.</param>
/// <param name="Slug">Its slug.</param>
public sealed record SiteCreatedEvent(Guid SiteId, string Slug) : CmsEvent;

/// <summary>A site, or its locales, was changed.</summary>
/// <param name="SiteId">The id of the site.</param>
/// <param name="Slug">Its slug.</param>
public sealed record SiteUpdatedEvent(Guid SiteId, string Slug) : CmsEvent;

/// <summary>A site was deleted. Its source names no site, since the site is gone.</summary>
/// <param name="SiteId">The id the site had.</param>
/// <param name="Slug">The slug it had.</param>
public sealed record SiteDeletedEvent(Guid SiteId, string Slug) : CmsEvent;

/// <summary>A plugin was enabled for a site.</summary>
/// <param name="Plugin">The name of the plugin.</param>
/// <param name="SiteId">The id of the site.</param>
public sealed record PluginEnabledEvent(string Plugin, Guid SiteId) : CmsEvent;

/// <summary>A plugin was disabled for a site.</summary>
/// <param name="Plugin">The name of the plugin.</param>
/// <param name="SiteId">The id of the site.</param>
public sealed record PluginDisabledEvent(string Plugin, Guid SiteId) : CmsEvent;

/// <summary>Cached output was evicted, for a CDN to purge too.</summary>
/// <param name="Tags">The cache tags that were evicted.</param>
public sealed record CacheInvalidatedEvent(IReadOnlyList<string> Tags) : CmsEvent;

/// <summary>An attempt of a job failed. It may be retried.</summary>
/// <param name="JobId">The id of the job.</param>
/// <param name="Type">The name the job is registered under.</param>
/// <param name="Queue">The queue.</param>
/// <param name="Attempt">The attempt that failed.</param>
/// <param name="MaxAttempts">The attempts the job may have.</param>
/// <param name="Error">What went wrong, without the payload.</param>
public sealed record JobFailedEvent(Guid JobId, string Type, string Queue, int Attempt, int MaxAttempts, string Error) : CmsEvent;

/// <summary>A job is out of attempts and will not run again unless someone retries it.</summary>
/// <param name="JobId">The id of the job.</param>
/// <param name="Type">The name the job is registered under.</param>
/// <param name="Queue">The queue.</param>
/// <param name="Attempts">The attempts it had.</param>
/// <param name="Error">What went wrong in the last one, without the payload.</param>
public sealed record JobDeadLetteredEvent(Guid JobId, string Type, string Queue, int Attempts, string Error) : CmsEvent;
