namespace FluentCMS.Core.Models;

public static class ModelErrorCodes
{
    public const string NotConfigured = "model_not_configured";
    public const string EndpointNotAllowed = "model_endpoint_not_allowed";
    public const string SpendCapExceeded = "llm_spend_cap_exceeded";
    public const string SpendUnknown = "llm_spend_unknown";
    public const string Unaccounted = "model_call_unaccounted";
    public const string CallFailed = "model_call_failed";
    public const string PeopleOnly = "model_purge_people_only";
}
