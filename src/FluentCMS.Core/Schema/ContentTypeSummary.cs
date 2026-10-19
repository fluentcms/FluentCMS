namespace FluentCMS.Core.Schema;

// Version is the latest version written; PublishedVersion is the live one, null until a change set publishes the type (SCH-05).
public sealed record ContentTypeSummary(Guid Id, string Name, string Slug, int Version, int? PublishedVersion);
