using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;

namespace FluentCMS.Infrastructure.Checks;

// One check run of a change set (CHK-03); its results are CheckResultRows. Inserted once and never changed.
public sealed class CheckRunRow
{
    public Guid Id { get; set; }

    public Guid ChangeSetId { get; set; }

    public int Number { get; set; }

    public CheckRunMode Mode { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public CheckStatus Status { get; set; }

    public decimal Cost { get; set; }

    // A JSON array of what kept the engine from checking an item, or the set.
    public string Problems { get; set; } = "[]";
}

// One check's verdict on one item of a run, with its findings as JSON and, for an LLM-judged check, the model call it rests on.
public sealed class CheckResultRow
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    // Items in operation order, and checks by name within an item.
    public int Position { get; set; }

    public ItemKind ItemKind { get; set; }

    public Guid ItemId { get; set; }

    public int ItemVersion { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public Guid CheckId { get; set; }

    public string Check { get; set; } = string.Empty;

    public int CheckVersion { get; set; }

    public string CheckType { get; set; } = string.Empty;

    public CheckKind Kind { get; set; }

    public CheckSeverity Severity { get; set; }

    public string Description { get; set; } = string.Empty;

    public CheckStatus Status { get; set; }

    public string Findings { get; set; } = "[]";

    public Guid? ModelCallId { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? PromptHash { get; set; }

    public decimal Cost { get; set; }
}
