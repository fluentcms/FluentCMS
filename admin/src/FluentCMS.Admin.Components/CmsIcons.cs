using Microsoft.FluentUI.AspNetCore.Components;
using Size20 = Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20;

namespace FluentCMS.Admin.Components;

internal static class CmsIcons
{
    public static Icon Get(CmsIconName name) => name switch
    {
        CmsIconName.Add => new Size20.Add(),
        CmsIconName.Delete => new Size20.Delete(),
        CmsIconName.Edit => new Size20.Edit(),
        CmsIconName.Save => new Size20.Save(),
        CmsIconName.Search => new Size20.Search(),
        CmsIconName.Settings => new Size20.Settings(),
        CmsIconName.Dismiss => new Size20.Dismiss(),
        CmsIconName.More => new Size20.MoreHorizontal(),
        CmsIconName.Upload => new Size20.ArrowUpload(),
        CmsIconName.Folder => new Size20.Folder(),
        CmsIconName.Document => new Size20.Document(),
        CmsIconName.Image => new Size20.Image(),
        CmsIconName.Person => new Size20.Person(),
        CmsIconName.Globe => new Size20.Globe(),
        CmsIconName.Info => new Size20.Info(),
        CmsIconName.Warning => new Size20.Warning(),
        CmsIconName.Checkmark => new Size20.Checkmark(),
        CmsIconName.Open => new Size20.Open(),
        CmsIconName.Sun => new Size20.WeatherSunny(),
        CmsIconName.Moon => new Size20.WeatherMoon(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };
}
