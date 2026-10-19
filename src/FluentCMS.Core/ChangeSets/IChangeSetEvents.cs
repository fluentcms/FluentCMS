using FluentCMS.Core.Extensions;

namespace FluentCMS.Core.ChangeSets;

// Told when a change set is published, inside the publish's unit of work, so what a listener records commits or rolls back with the
// publish (API-05). A revert's publish also names the set it reverted. Publishing never waits on a listener: one queues its work, such
// as a webhook delivery, rather than doing it, and fails only when the database does.
[ExtensionPoint]
public interface IChangeSetEvents
{
    Task PublishedAsync(ChangeSet published, ChangeSet? reverted, CancellationToken cancellationToken = default);
}
