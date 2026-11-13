using FluentCMS.Transfer;

namespace FluentCMS.Core.Transfer;

// What an import did, in the order it did it, so that a dry run and the import that follows give the same report. Every item
// is counted by section, and only the conflicts and errors are listed, up to FluentCms:Transfer:MaxReportItems, so that the
// answer does not grow with the entries that were imported.
internal sealed class TransferReport(bool dryRun, int maxItems)
{
    private readonly List<TransferItem> _items = [];
    private readonly Dictionary<string, TransferCounts> _sections = new(StringComparer.Ordinal);

    public bool DryRun => dryRun;

    public IReadOnlyList<TransferItem> Items => _items;

    public IReadOnlyDictionary<string, TransferCounts> Sections => _sections;

    public TransferCounts Total => TransferCounts.Sum(_sections.Values);

    public void Add(string section, string id, TransferOutcome outcome, string? code = null, string? message = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentNullException.ThrowIfNull(id);
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "An outcome is one of TransferOutcome.");
        }

        _sections[section] = (_sections.GetValueOrDefault(section) ?? TransferCounts.None).With(outcome);
        if (outcome is TransferOutcome.Conflict or TransferOutcome.Error && _items.Count < maxItems)
        {
            _items.Add(new TransferItem(section, id, outcome, code, message));
        }
    }

    // The error that stopped the import, listed whatever the number of items already listed.
    public void Stop(string section, string id, string code, string message)
    {
        _sections[section] = (_sections.GetValueOrDefault(section) ?? TransferCounts.None).With(TransferOutcome.Error);
        _items.Add(new TransferItem(section, id, TransferOutcome.Error, code, message));
    }

    public TransferCounts Of(string section) => _sections.GetValueOrDefault(section) ?? TransferCounts.None;

    public TransferResult ToResult() => new(dryRun, Total, _sections, _items);
}

internal sealed record TransferItem(string Section, string Id, TransferOutcome Outcome, string? Code, string? Message);

internal sealed record TransferCounts(int Created, int Updated, int Skipped, int Conflicts, int Errors)
{
    public static readonly TransferCounts None = new(0, 0, 0, 0, 0);

    public TransferCounts With(TransferOutcome outcome) => outcome switch
    {
        TransferOutcome.Created => this with { Created = Created + 1 },
        TransferOutcome.Updated => this with { Updated = Updated + 1 },
        TransferOutcome.Skipped => this with { Skipped = Skipped + 1 },
        TransferOutcome.Conflict => this with { Conflicts = Conflicts + 1 },
        _ => this with { Errors = Errors + 1 },
    };

    public static TransferCounts Sum(IEnumerable<TransferCounts> counts) => counts.Aggregate(
        None,
        (sum, item) => new TransferCounts(sum.Created + item.Created, sum.Updated + item.Updated, sum.Skipped + item.Skipped, sum.Conflicts + item.Conflicts, sum.Errors + item.Errors));
}

// The last line of an import's answer.
internal sealed record TransferResult(bool DryRun, TransferCounts Total, IReadOnlyDictionary<string, TransferCounts> Sections, IReadOnlyList<TransferItem> Items);
