namespace FluentCMS.Plugins.MultiSite;

// The multi-site plugin has the codes from FCMS1660 up to 1679. Each is added with AddErrorCode in MultiSitePlugin.
internal static class MultiSiteErrorCodes
{
    public const string InvalidHostName = "FCMS1660";
    public const string WildcardHostName = "FCMS1661";
    public const string HostNameWithPort = "FCMS1662";
    public const string HostNameTaken = "FCMS1663";
    public const string HostNotFound = "FCMS1664";
    public const string PrimaryHostNeeded = "FCMS1665";
    public const string HostNamesBusy = "FCMS1666";
}
