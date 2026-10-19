namespace FluentCMS.Core.Audit;

public static class AuditErrorCodes
{
    public const string WindowInverted = "audit_window_inverted";
    public const string SubjectTypeMissing = "audit_subject_type_missing";
    public const string SubjectIdMissing = "audit_subject_id_missing";
    public const string ExportFormatUnknown = "audit_export_format_unknown";
    public const string OperationsMissing = "audit_operations_missing";

    // The same codes the other lists of the desk use for a window out of range.
    public const string OffsetInvalid = "offset_invalid";
    public const string LimitInvalid = "limit_invalid";
}
