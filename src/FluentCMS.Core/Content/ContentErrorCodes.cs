namespace FluentCMS.Core.Content;

public static class ContentErrorCodes
{
    public const string FieldsMalformed = "fields_malformed";
    public const string FieldUnknown = "field_unknown";
    public const string FieldRequired = "field_required";
    public const string EntryNotFound = "entry_not_found";
    public const string VersionNotFound = "version_not_found";
    public const string VersionConflict = "version_conflict";
    public const string ReferenceNotFound = "reference_not_found";
    public const string ReferenceTypeMismatch = "reference_type_mismatch";
    public const string EntryNotEntity = "entry_not_entity";
    public const string EntryNotDeleted = "entry_not_deleted";
    public const string RetentionExpired = "retention_expired";
    public const string EntryNotPublished = "entry_not_published";
}
