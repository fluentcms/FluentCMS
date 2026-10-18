namespace FluentCMS.Core.ChangeSets;

public sealed record NewChangeSet(string Rationale, IReadOnlyList<string> Sources);
