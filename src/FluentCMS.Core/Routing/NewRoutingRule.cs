namespace FluentCMS.Core.Routing;

public sealed record NewRoutingRule(string Name, RoutingRuleDefinition Definition);

public sealed record RoutingRuleSummary(Guid Id, string Name, int LatestVersion, int? ActiveVersion);
