namespace FluentCMS.Core.Checks;

public sealed record CheckSummary(Guid Id, string Name, int LatestVersion, int? ActiveVersion);
