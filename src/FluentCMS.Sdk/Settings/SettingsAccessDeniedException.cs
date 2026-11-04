using FluentCMS.Sites;

namespace FluentCMS.Settings;

/// <summary>Thrown when the principal may not change a settings area. The HTTP layer maps it to a 401 or a 403.</summary>
public sealed class SettingsAccessDeniedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="area">The name of the settings area.</param>
    /// <param name="site">The site whose settings were to change, or <see langword="null"/> for the instance defaults.</param>
    public SettingsAccessDeniedException(string area, SiteId? site)
        : base(site is null
            ? $"Changing the instance defaults of the settings area '{area}' needs the permission 'settings.{area}.manage' with no site limit."
            : $"Changing the settings area '{area}' of site {site} needs the permission 'settings.{area}.manage' for that site.")
    {
        Area = area;
        Site = site;
    }

    /// <summary>The name of the settings area.</summary>
    public string Area { get; }

    /// <summary>The site, or <see langword="null"/> for the instance defaults.</summary>
    public SiteId? Site { get; }
}
