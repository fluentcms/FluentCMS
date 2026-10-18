namespace FluentCMS.Core.Agents;

public static class AgentCredentialErrorCodes
{
    public const string DisplayNameMissing = "display_name_missing";
    public const string DisplayNameTooLong = "display_name_too_long";
    public const string ExpiryNotInFuture = "expiry_not_in_future";
    public const string ScopeSitesMissing = "scope_sites_missing";
    public const string ScopeSiteInvalid = "scope_site_invalid";
    public const string ScopeSiteUnknown = "scope_site_unknown";
    public const string ScopeTypesEmpty = "scope_types_empty";
    public const string ScopeTypeInvalid = "scope_type_invalid";
    public const string ScopeFieldsEmpty = "scope_fields_empty";
    public const string ScopeFieldInvalid = "scope_field_invalid";
    public const string ScopeFieldOutsideTypes = "scope_field_outside_types";
    public const string ScopeOperationsMissing = "scope_operations_missing";
    public const string ScopeOperationInvalid = "scope_operation_invalid";
    public const string CapInvalid = "cap_invalid";
    public const string CredentialNotFound = "agent_credential_not_found";
    public const string AlreadyRevoked = "agent_credential_already_revoked";
    public const string PeopleOnly = "agent_credential_people_only";
    public const string KeyInvalid = "agent_key_invalid";
    public const string Expired = "agent_credential_expired";
    public const string Revoked = "agent_credential_revoked";
}
