using FluentCMS.Transfer;

namespace FluentCMS.Core.Transfer;

// What an import did, in the order it did it, so that a dry run and the import that follows give the same report. Every item
// is counted by section, and only the conflicts and errors are listed, up to FluentCms:Transfer:MaxReportItems and each held
// to MaxTextLength, so that the answer grows neither with the entries that were imported nor with what a line holds.
internal sealed class TransferReport(bool dryRun, int maxItems)
{
    public const int MaxTextLength = 4096;

    private readonly List<TransferItem> _items = [];
    private readonly Dictionary<string, TransferCounts> _sections = new(StringComparer.Ordinal);
    private int _omitted;

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
        if (outcome is not (TransferOutcome.Conflict or TransferOutcome.Error))
        {
            return;
        }

        if (_items.Count < maxItems)
        {
            _items.Add(new TransferItem(Cut(section), Cut(id), outcome, code, message is null ? null : Cut(message)));
        }
        else
        {
            _omitted++;
        }
    }

    // The error that stopped the import, listed whatever the number of items already listed.
    public void Stop(string section, string id, string code, string message)
    {
        _sections[section] = (_sections.GetValueOrDefault(section) ?? TransferCounts.None).With(TransferOutcome.Error);
        _items.Add(new TransferItem(section, Cut(id), TransferOutcome.Error, code, Cut(message)));
    }

    public TransferCounts Of(string section) => _sections.GetValueOrDefault(section) ?? TransferCounts.None;

    public TransferResult ToResult() => new(dryRun, Total, _sections, _items, _omitted);

    private static string Cut(string text)
    {
        if (text.Length <= MaxTextLength)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
        return string.Concat(text.AsSpan(0, length), "…");
    }
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

// The last line of an import's answer. Omitted is the number of conflicts and errors that Items leaves out.
internal sealed record TransferResult(bool DryRun, TransferCounts Total, IReadOnlyDictionary<string, TransferCounts> Sections, IReadOnlyList<TransferItem> Items, int Omitted);
