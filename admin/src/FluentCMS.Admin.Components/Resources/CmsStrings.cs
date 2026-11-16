using System.Globalization;
using System.Resources;

namespace FluentCMS.Admin.Components.Resources;

// Read in the current UI culture, so the culture choice of the admin (spec 4.17) applies to the wrapped set too.
internal static class CmsStrings
{
    private static readonly ResourceManager Manager = new("FluentCMS.Admin.Components.Resources.CmsStrings", typeof(CmsStrings).Assembly);

    public static string GridSortedAscending => Get(nameof(GridSortedAscending));

    public static string GridSortedDescending => Get(nameof(GridSortedDescending));

    public static string GridRowsSelected => Get(nameof(GridRowsSelected));

    public static string DropZoneChoose => Get(nameof(DropZoneChoose));

    public static string Close => Get(nameof(Close));

    public static string Format(string format, object? argument) => string.Format(CultureInfo.CurrentCulture, format, argument);

    private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
