namespace FluentCMS.Core.Authorization;

// The claims an HTTP authentication handler puts on the request's ClaimsPrincipal. The API layer turns them into a Principal.
internal static class CmsClaimTypes
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Kind = "fcms_kind";
    public const string Roles = "roles";
    public const string Scope = "scope";
    public const string Sites = "fcms_sites";
    public const string Types = "fcms_types";
    public const string RateLimit = "fcms_rate_limit";

    // The one entry, and the one locale, a preview token reads.
    public const string Entry = "fcms_entry";
    public const string Locale = "fcms_locale";

    // The OAuth client a bearer token was issued to; the request's OperationContext.ClientId.
    public const string ClientId = "client_id";

    // How a user's bearer token was signed in to, such as "mfa"; a key or a service account has none.
    public const string AuthenticationMethods = "amr";
}
