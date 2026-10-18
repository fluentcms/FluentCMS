using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;

namespace FluentCMS.Tests.ChangeSets;

internal static class TestChangeSets
{
    public static readonly Guid Site = Guid.NewGuid();

    public static readonly Initiator Agent = new AgentInitiator(Guid.NewGuid(), "example-agent");

    public static ChangeSet Open()
    {
        return ChangeSet.Open(Site, "Sync prices from the catalog export.", ["exports/catalog-2026-10-18.csv"], Agent).Value;
    }

    public static ChangeOperation Update(ItemKind itemKind, Guid? itemId = null)
    {
        return ChangeOperation.Create(OperationKind.Update, itemKind, itemId ?? Guid.NewGuid(), 1).Value;
    }

    // A set with one operation, moved along the shortest path of the transition table to the given state.
    public static ChangeSet InState(ChangeSetState state)
    {
        var changeSet = Open();
        Assert.True(changeSet.AddOperation(Update(ItemKind.Entry)).IsSuccess);

        foreach (var next in PathFromOpen(state))
        {
            var moved = next == ChangeSetState.Rejected ? changeSet.Reject("Example reason.") : changeSet.TransitionTo(next);
            Assert.True(moved.IsSuccess);
        }

        Assert.Equal(state, changeSet.State);
        return changeSet;
    }

    private static List<ChangeSetState> PathFromOpen(ChangeSetState target)
    {
        var previous = new Dictionary<ChangeSetState, ChangeSetState>();
        var queue = new Queue<ChangeSetState>([ChangeSetState.Open]);

        while (queue.TryDequeue(out var state) && state != target)
        {
            foreach (var next in ChangeSetTransitions.From(state))
            {
                if (next != ChangeSetState.Open && previous.TryAdd(next, state))
                {
                    queue.Enqueue(next);
                }
            }
        }

        var path = new List<ChangeSetState>();
        for (var state = target; state != ChangeSetState.Open; state = previous[state])
        {
            path.Insert(0, state);
        }

        return path;
    }
}
