using FluentCMS.Core.Results;

namespace FluentCMS.Core.Audit;

public enum AuditExportFormat
{
    Json,
    Csv,
}

public interface IAuditExporter
{
    // Writes what the query selects, in reading order, while it is being read; the destination is flushed and left open.
    // First commits an "audit.exported" event naming the current initiator and, for one item's history, the item (AUD-01).
    // Fails before recording or writing anything when the query does not validate or the format is unknown.
    Task<Result> ExportAsync(AuditQuery query, AuditExportFormat format, Stream destination, CancellationToken cancellationToken = default);
}
