namespace FluentCMS.Core.Content;

public sealed record EntryVersionSummary(int Version, int ContentTypeVersion, DateTimeOffset CreatedAt);
