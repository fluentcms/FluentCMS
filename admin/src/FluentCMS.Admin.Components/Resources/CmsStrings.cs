using System.Globalization;
using System.Resources;

namespace FluentCMS.Admin.Components.Resources;

// The wrapped set's own reading of its resources, in the current UI culture, so the culture choice of the admin (spec 4.17)
// applies to it too. A host that reads them through its own localizer registers another ICmsTextSource.
internal sealed class CmsStrings : ICmsTextSource
{
    public const string GridSortedBy = nameof(GridSortedBy);
    public const string GridSortAscending = nameof(GridSortAscending);
    public const string GridSortDescending = nameof(GridSortDescending);
    public const string GridSortThen = nameof(GridSortThen);
    public const string GridSortLevel = nameof(GridSortLevel);
    public const string GridSortHint = nameof(GridSortHint);
    public const string GridSelectAll = nameof(GridSelectAll);
    public const string GridSelectRow = nameof(GridSelectRow);
    public const string GridLoading = nameof(GridLoading);
    public const string GridEmpty = nameof(GridEmpty);
    public const string GridRowsSelected = nameof(GridRowsSelected);
    public const string DropZoneChoose = nameof(DropZoneChoose);
    public const string Close = nameof(Close);
    public const string InputCharacterCount = nameof(InputCharacterCount);
    public const string InputDateInvalid = nameof(InputDateInvalid);
    public const string InputDateTooEarly = nameof(InputDateTooEarly);
    public const string InputDateTooLate = nameof(InputDateTooLate);
    public const string InputTimeInvalid = nameof(InputTimeInvalid);
    public const string InputNumberInvalid = nameof(InputNumberInvalid);
    public const string InputWholeNumberInvalid = nameof(InputWholeNumberInvalid);
    public const string InputNumberNotNegative = nameof(InputNumberNotNegative);
    public const string SelectNone = nameof(SelectNone);
    public const string SecretOnce = nameof(SecretOnce);
    public const string SecretHidden = nameof(SecretHidden);
    public const string SecretShow = nameof(SecretShow);
    public const string SecretHide = nameof(SecretHide);
    public const string SecretCopy = nameof(SecretCopy);
    public const string SecretCopied = nameof(SecretCopied);
    public const string SecretNotCopied = nameof(SecretNotCopied);
    public const string SecretDone = nameof(SecretDone);
    public const string SecretLeaveQuestion = nameof(SecretLeaveQuestion);
    public const string SecretStay = nameof(SecretStay);
    public const string SecretLeave = nameof(SecretLeave);

    private static readonly ResourceManager Manager = new("FluentCMS.Admin.Components.Resources.CmsStrings", typeof(CmsStrings).Assembly);

    public string? GetText(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture);

    public static string Format(string format, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, format, arguments);
}

internal static class CmsTextSourceExtensions
{
    public static string Text(this ICmsTextSource source, string name) => source.GetText(name) ?? name;
}
