using FluentCMS.Admin.Shell;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Administration.Settings;

// The name of an area as the menu and its page show it: Settings.Area.Name.{area} of the area's resources when they have
// one, as for the areas of core and of the first-party plugins, else the area's name made readable.
internal static class SettingsLabels
{
    public static string Area(string area, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(localizer);
        var resource = localizer[$"Settings.Area.Name.{area}"];
        return resource.ResourceNotFound ? AdminLabels.Readable(area) : resource.Value;
    }
}
