namespace FluentCMS.Plugins.Media;

// The media plugin has the codes from FCMS1580 up to 1599. Each is added with AddErrorCode in MediaPlugin.
internal static class MediaErrorCodes
{
    public const string InvalidField = "FCMS1580";
    public const string TypeNotAllowed = "FCMS1581";
    public const string TooLarge = "FCMS1582";
    public const string Executable = "FCMS1583";
    public const string SvgRefused = "FCMS1584";
    public const string AssetNotFound = "FCMS1585";
    public const string FolderNotFound = "FCMS1586";
    public const string FolderNameTaken = "FCMS1587";
    public const string FolderNotEmpty = "FCMS1588";
    public const string FolderCycle = "FCMS1589";
}
