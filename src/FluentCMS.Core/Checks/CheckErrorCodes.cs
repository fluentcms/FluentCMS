using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Checks;

public static class CheckErrorCodes
{
    public const string DefinitionMalformed = "definition_malformed";
    public const string CheckTypeUnknown = "check_type_unknown";
    public const string SettingInvalid = SchemaErrorCodes.SettingInvalid;
    public const string NameInvalid = "check_name_invalid";
    public const string NameTaken = "check_name_taken";
    public const string SiteMissing = "site_missing";
    public const string DescriptionInvalid = "description_invalid";
    public const string SeverityInvalid = "severity_invalid";
    public const string ScopeInvalid = "scope_invalid";
    public const string ExamplesMissing = "examples_missing";
    public const string ExampleInvalid = "example_invalid";
    public const string ExampleFailed = "example_failed";
    public const string CheckNotFound = "check_not_found";
    public const string VersionNotFound = "version_not_found";
    public const string VersionConflict = "version_conflict";
    public const string AlreadyActive = "check_already_active";
    public const string NotActive = "check_not_active";
    public const string PeopleOnly = "check_people_only";
}
