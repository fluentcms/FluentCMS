namespace FluentCMS.Core.Content;

public sealed record EntrySummary(Guid Id, int LatestVersion, int? PublishedVersion, EntryState State);
