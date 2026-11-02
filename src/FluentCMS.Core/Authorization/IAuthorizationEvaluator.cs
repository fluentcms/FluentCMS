using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// The one enforcement point of spec 2.3: nothing else reads permission data.
internal interface IAuthorizationEvaluator
{
    // The principal's effective permissions, loaded once per context in a request. EffectivePermissions answers the
    // operation, field and row rule questions without another query.
    ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default);

    // The operation question with an entry: the permission, the entry being in the target's site, the row rule on the
    // entry's row in this state and locale, and for reading a draft the draft check. A write reads the rule against the
    // draft, which is the row it changes. An entry that does not exist is refused.
    ValueTask<bool> CanAccessEntryAsync(
        OperationContext context,
        SchemaType type,
        ContentOperation operation,
        Guid entry,
        AccessTarget target,
        QueryStatus status = QueryStatus.Published,
        CancellationToken cancellationToken = default);

    // Which fields a write may touch: a field counts when a grant that allows it also matches the entry. A create has
    // no entry at first, so a row rule on its grant holds nothing back until it is asked again with the new entry; any
    // other write without an entry gets only what grants with no row rule allow.
    ValueTask<WritableFields> WritableFieldsAsync(
        OperationContext context,
        SchemaType type,
        ContentOperation operation,
        Guid? entry,
        AccessTarget target,
        CancellationToken cancellationToken = default);
}
