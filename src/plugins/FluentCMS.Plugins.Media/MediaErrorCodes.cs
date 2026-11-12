namespace FluentCMS.Plugins.Media;

// The media plugin has the codes from FCMS1580 up to 1599, and FCMS3220 and 3221 of transfer. Each is added with AddErrorCode in MediaPlugin.
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
    public const string SignatureInvalid = "FCMS1590";
    public const string TransformLimit = "FCMS1591";
    public const string NotTransformable = "FCMS1592";
    public const string PresetNotFound = "FCMS1593";
    public const string AssetInUse = "FCMS1594";
    public const string DownloadExpired = "FCMS1595";

    // The items of a transfer package, from the range of transfer, 3200 up to 3239.
    public const string TransferFileMissing = "FCMS3220";
    public const string TransferIdTaken = "FCMS3221";
}
