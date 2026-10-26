using FluentCMS.Core.Results;

namespace FluentCMS.Core.Routing;

public interface IChangeSetRouting
{
    // Where the active routing rules send a stored set, from its items, their changed fields and its initiator (RTE-01). A read: it
    // changes nothing and appends no event; acting on the decision is the caller's.
    Task<Result<RoutingDecision>> RouteAsync(Guid changeSetId, CancellationToken cancellationToken = default);
}
