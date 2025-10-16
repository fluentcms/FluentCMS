namespace FluentCMS.Api.Plugins.IdentityManagement;

internal class SecurityContextResolver(IHttpContextAccessor httpContextAccessor)
{
    public const string SESSION_ID_HEADER_KEY = "X_Session_Id";
    public const string UNIQUE_USER_ID_HEADER_KEY = "X-Unique-Id";
    public const string DEFAULT_LANGUAGE = "en-US";
    public const string USER_IP_FORWARDED_HEADER_KEY = "X-Forwarded-For";

    public ISecurityContext Resolve()
    {
        var accessor = httpContextAccessor ??
            throw new ArgumentNullException(nameof(httpContextAccessor));

        var context = accessor.HttpContext;
        // If context is null, return a default SystemSecurityContext
        if (context == null)
            return new SystemSecurityContext();

        var traceId = context.TraceIdentifier ?? string.Empty;
        var uniqueId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(UNIQUE_USER_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty;
        var sessionId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(SESSION_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty;
        var userIp = context.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
        var language = context.Request?.GetTypedHeaders().AcceptLanguage.FirstOrDefault()?.Value.Value ?? DEFAULT_LANGUAGE;
        // If behind a proxy, try to get the original client IP from the X-Forwarded-For header
        var clientIp = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(USER_IP_FORWARDED_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? userIp;
        Guid? userId = null;
        var username = string.Empty;
        var isAuthenticated = false;
        var user = accessor?.HttpContext?.User;
        // Retrieve the user claims principal from the context
        if (user != null)
        {
            // Extract and parse the user ID from claims (ClaimTypes.Sid)
            var idClaimValue = user.FindFirstValue(ClaimTypes.Sid);
            userId = idClaimValue == null ? null : Guid.Parse(idClaimValue);
            // Extract the username from claims (ClaimTypes.NameIdentifier)
            username = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            // Determine if the user is authenticated
            isAuthenticated = user.Identity?.IsAuthenticated ?? false;
        }
        var instance = new ApiSecurityContext
        {
            TraceId = traceId,
            UniqueId = uniqueId,
            SessionId = sessionId,
            UserIp = userIp,
            Language = language,
            ClientIp = clientIp,
            UserId = userId,
            Username = username,
            IsAuthenticated = isAuthenticated,
        };
        return instance;
    }
}
