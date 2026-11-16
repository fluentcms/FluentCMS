namespace FluentCMS.Admin.Api;

/// <summary>The content locale of the view, which the API client sends as <c>X-Cms-Locale</c>. The locale switcher fills it.</summary>
public interface IContentLocale
{
    /// <summary>The BCP 47 tag, or <see langword="null"/> for the site's default locale.</summary>
    string? Locale { get; }
}
