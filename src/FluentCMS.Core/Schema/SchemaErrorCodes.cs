namespace FluentCMS.Core.Schema;

public static class SchemaErrorCodes
{
    public const string DefinitionMalformed = "definition_malformed";
    public const string FieldNameInvalid = "field_name_invalid";
    public const string FieldNameDuplicate = "field_name_duplicate";
    public const string FieldTypeUnknown = "field_type_unknown";
    public const string SettingMissing = "setting_missing";
    public const string SettingInvalid = "setting_invalid";
    public const string SettingUnknown = "setting_unknown";
    public const string NameInvalid = "name_invalid";
    public const string SlugInvalid = "slug_invalid";
    public const string SiteMissing = "site_missing";
    public const string SlugTaken = "slug_taken";
    public const string ReferenceTargetUnknown = "reference_target_unknown";
    public const string ContentTypeNotFound = "content_type_not_found";
    public const string VersionNotFound = "version_not_found";
    public const string VersionConflict = "version_conflict";
    public const string EntityFlagRemoved = "entity_flag_removed";
    public const string ReferenceTargetNotEntity = "reference_target_not_entity";
    public const string ReferenceFieldUnknown = "reference_field_unknown";
    public const string FieldReferenced = "field_referenced";
}
