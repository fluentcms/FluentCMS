namespace FluentCMS.Admin.Configuration;

/// <summary>
/// The runtime configuration of the admin, read from <c>config.json</c> at start and validated before anything else runs. It is
/// registered as a singleton only when the file is valid, so a component or service that takes it never sees an invalid one.
/// </summary>
public sealed record AdminConfiguration
{
    private readonly Uri? _authority;

    /// <summary>
    /// The origin and path base of the API host, such as <c>https://cms.example.com/</c> or <c>https://example.com/cms/</c>. Read
    /// from <c>config.json</c>, it always ends with a slash, so that a relative address resolves under the path base.
    /// </summary>
    public required Uri ApiBaseUrl { get; init; }

    /// <summary>
    /// The issuer of the authorization server, as <c>config.json</c> wrote it, without a slash added, since the issuer of the
    /// discovery document is compared as an exact string with <see cref="Uri.OriginalString"/>. When not set, it is
    /// <c>apiBaseUrl</c> as the file wrote it; a record built in code without it falls back to <see cref="ApiBaseUrl"/>.
    /// </summary>
    public Uri Authority
    {
        get => _authority ?? ApiBaseUrl;
        init => _authority = value;
    }

    /// <summary>The OAuth client id of the admin; <c>fluentcms-admin</c> when not set.</summary>
    public string ClientId { get; init; } = "fluentcms-admin";

    /// <summary>The scopes asked at sign-in; <c>openid profile email offline_access management</c> when not set.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = ["openid", "profile", "email", "offline_access", "management"];

    /// <summary>The name, logo, favicon and accent color of the admin.</summary>
    public AdminBranding Branding { get; init; } = new();

    /// <summary>The interface cultures offered, and the default one.</summary>
    public AdminCultures Cultures { get; init; } = new();

    /// <summary>
    /// The origins the preview frame may load, each as <c>scheme://host[:port]</c> without a trailing slash. Empty when not set,
    /// which turns preview off.
    /// </summary>
    public IReadOnlyList<string> PreviewOrigins { get; init; } = [];

    /// <summary>Whether preview is on, which it is when at least one origin is configured.</summary>
    public bool PreviewEnabled => PreviewOrigins.Count > 0;
}
