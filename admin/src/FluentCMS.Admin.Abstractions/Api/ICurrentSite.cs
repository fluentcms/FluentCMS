namespace FluentCMS.Admin.Api;

/// <summary>The site the shell shows, which the API client sends as <c>X-Cms-Site</c>. The shell's site switcher fills it.</summary>
public interface ICurrentSite
{
    /// <summary>The id or slug of the site, or <see langword="null"/> for the instance's default site.</summary>
    string? Site { get; }
}
