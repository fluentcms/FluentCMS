using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// The one enforcement point of spec 2.3: nothing else reads permission data.
internal interface IAuthorizationEvaluator
{
    // The principal's effective permissions, loaded once per context in a request. EffectivePermissions answers the
    // operation, field and row rule questions without another query.
    ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default);

    // The operation question with an entry: the permission, its row rule on that entry's row in this state and locale,
    // and for reading a draft the draft check. A write reads the rule against the draft, which is the row it changes.
    ValueTask<bool> CanAccessEntryAsync(
        OperationContext context,
        SchemaType type,
        ContentOperation operation,
        Guid entry,
        AccessTarget target,
        QueryStatus status = QueryStatus.Published,
        CancellationToken cancellationToken = default);
}
