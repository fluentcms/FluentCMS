namespace FluentCMS.Core.Schema;

public sealed record ContentTypeSummary(Guid Id, string Name, string Slug, int Version);
