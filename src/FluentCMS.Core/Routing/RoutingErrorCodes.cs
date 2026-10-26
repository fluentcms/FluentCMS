namespace FluentCMS.Core.Routing;

public static class RoutingErrorCodes
{
    public const string DefinitionMalformed = "definition_malformed";
    public const string NameInvalid = "routing_rule_name_invalid";
    public const string NameTaken = "routing_rule_name_taken";
    public const string SiteMissing = "site_missing";
    public const string DescriptionInvalid = "description_invalid";
    public const string ConditionInvalid = "condition_invalid";
    public const string DispositionInvalid = "disposition_invalid";
    public const string RuleNotFound = "routing_rule_not_found";
    public const string VersionNotFound = "version_not_found";
    public const string VersionConflict = "version_conflict";
    public const string AlreadyActive = "routing_rule_already_active";
    public const string NotActive = "routing_rule_not_active";
    public const string PeopleOnly = "routing_rule_people_only";
}
