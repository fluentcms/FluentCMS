using FluentCMS.Admin;

namespace FluentCMS.Host;

internal sealed class CookieThemePreference(IHttpContextAccessor accessor) : IThemePreference
{
    public string? Current => ThemeCookie.Parse(accessor.HttpContext?.Request.Cookies[ThemeCookie.Name]);
}
