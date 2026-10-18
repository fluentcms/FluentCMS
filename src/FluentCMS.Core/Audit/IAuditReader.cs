using FluentCMS.Core.Results;

namespace FluentCMS.Core.Audit;

public interface IAuditReader
{
    // Oldest first, ties broken by id. The events stream from the store as they are enumerated, so a selection of any size
    // reads in bounded memory. Fails without reading when the query does not validate.
    Result<IAsyncEnumerable<AuditEvent>> Read(AuditQuery query);
}
