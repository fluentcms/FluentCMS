namespace FluentCMS.Core.Webhooks;

public static class WebhookErrorCodes
{
    public const string UrlInvalid = "webhook_url_invalid";
    public const string UrlTooLong = "webhook_url_too_long";
    public const string UrlHasCredentials = "webhook_url_has_credentials";
    public const string UrlNotHttps = "webhook_url_not_https";
    public const string HostNotAllowed = "webhook_host_not_allowed";
    public const string EventsMissing = "webhook_events_missing";
    public const string EventUnknown = "webhook_event_unknown";
    public const string NotFound = "webhook_not_found";
    public const string AlreadyDisabled = "webhook_already_disabled";
    public const string Disabled = "webhook_disabled";
    public const string PeopleOnly = "webhook_people_only";
}
