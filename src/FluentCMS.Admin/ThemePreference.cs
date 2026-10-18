namespace FluentCMS.Admin;

// The theme picked with the header toggle: "light", "dark", or null to follow the system preference.
// The host reads it from the request, so the root element has its class before anything runs in the browser.
public interface IThemePreference
{
    string? Current { get; }
}

// admin.js keeps the choice in this cookie; any other value is no choice.
public static class ThemeCookie
{
    public const string Name = "fluentcms-theme";

    public static string? Parse(string? value) => value is "light" or "dark" ? value : null;
}
