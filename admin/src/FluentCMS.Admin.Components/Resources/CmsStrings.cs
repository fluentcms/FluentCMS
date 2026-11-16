using System.Globalization;
using System.Resources;

namespace FluentCMS.Admin.Components.Resources;

// The wrapped set's own reading of its resources, in the current UI culture, so the culture choice of the admin (spec 4.17)
// applies to it too. A host that reads them through its own localizer registers another ICmsTextSource.
internal sealed class CmsStrings : ICmsTextSource
{
    public const string GridSortedAscending = nameof(GridSortedAscending);
    public const string GridSortedDescending = nameof(GridSortedDescending);
    public const string GridRowsSelected = nameof(GridRowsSelected);
    public const string DropZoneChoose = nameof(DropZoneChoose);
    public const string Close = nameof(Close);

    private static readonly ResourceManager Manager = new("FluentCMS.Admin.Components.Resources.CmsStrings", typeof(CmsStrings).Assembly);

    public string? GetText(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture);

    public static string Format(string format, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, format, arguments);
}

internal static class CmsTextSourceExtensions
{
    public static string Text(this ICmsTextSource source, string name) => source.GetText(name) ?? name;
}
