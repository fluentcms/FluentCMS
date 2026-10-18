namespace FluentCMS.Core.Content;

// A soft-deleted entry, which restore brings back with its history until RestorableUntil (CNT-06, REL-03).
public sealed record DeletedEntry(Guid Id, string ContentType, int LatestVersion, DateTimeOffset DeletedAt, DateTimeOffset RestorableUntil);
