using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api;

public class ApiExecutionContext
{
    public const string SESSION_ID_HEADER_KEY = "sid";
    public const string UNIQUE_USER_ID_HEADER_KEY = "uid";
    public const string DEFAULT_LANGUAGE = "en-US";

    public string TraceId { get; set; } = string.Empty;
    public string UniqueId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UserIp { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    public ApiExecutionContext(IHttpContextAccessor accessor)
    {
        if (accessor?.HttpContext == null)
            return;

        var context = accessor.HttpContext;

        TraceId = context.TraceIdentifier;
        UniqueId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(UNIQUE_USER_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty;
        SessionId = context.Request?.Headers?.FirstOrDefault(_ => _.Key.Equals(SESSION_ID_HEADER_KEY, StringComparison.OrdinalIgnoreCase)).Value.ToString() ?? string.Empty;
        UserIp = context.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
        Language = context.Request?.GetTypedHeaders().AcceptLanguage.FirstOrDefault()?.Value.Value ?? DEFAULT_LANGUAGE;
    }
}
