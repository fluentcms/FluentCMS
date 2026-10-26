namespace FluentCMS.Core.Routing;

public enum DispositionKind
{
    AutoPublish,
    Review,
    Block,
}

// What happens to a change set after its checks (RTE-01): it publishes without a person, waits for N approvals, or cannot be
// approved at all.
public sealed record RoutingDisposition
{
    public const int MaxApprovals = 10;

    private RoutingDisposition(DispositionKind kind, int approvals)
    {
        Kind = kind;
        Approvals = approvals;
    }

    public static RoutingDisposition AutoPublish { get; } = new(DispositionKind.AutoPublish, 0);

    public static RoutingDisposition Block { get; } = new(DispositionKind.Block, 0);

    public DispositionKind Kind { get; }

    // How many approvals a review needs (RTE-05); zero for the other kinds.
    public int Approvals { get; }

    public static RoutingDisposition Review(int approvals)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(approvals, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(approvals, MaxApprovals);

        return new RoutingDisposition(DispositionKind.Review, approvals);
    }

    // Block is stricter than any review, and a review that needs more approvals is stricter than one that needs fewer.
    public bool IsStricterThan(RoutingDisposition other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Rank > other.Rank;
    }

    public override string ToString()
    {
        return Kind switch
        {
            DispositionKind.AutoPublish => "auto-publish",
            DispositionKind.Block => "block",
            _ => Approvals == 1 ? "review with 1 approval" : $"review with {Approvals} approvals",
        };
    }

    private int Rank => Kind switch
    {
        DispositionKind.AutoPublish => 0,
        DispositionKind.Review => Approvals,
        _ => MaxApprovals + 1,
    };
}
