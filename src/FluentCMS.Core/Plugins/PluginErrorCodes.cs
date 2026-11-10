namespace FluentCMS.Core.Plugins;

// The codes up to 1599 that are not listed here are kept for later plugin work.
internal static class PluginErrorCodes
{
    public const string InvalidPlugin = "FCMS1501";
    public const string InvalidName = "FCMS1502";
    public const string InvalidVersion = "FCMS1503";
    public const string InvalidSdkRange = "FCMS1504";
    public const string SdkNotSatisfied = "FCMS1505";
    public const string DuplicateName = "FCMS1506";
    public const string MissingDependency = "FCMS1507";
    public const string DependencyCycle = "FCMS1508";

    public const string DuplicateFieldType = "FCMS1521";
    public const string InvalidErrorCode = "FCMS1522";
    public const string TableOutsidePrefix = "FCMS1523";
    public const string InvalidEndpointPrefix = "FCMS1524";
    public const string SecondMigrations = "FCMS1525";
    public const string RequiredFieldReference = "FCMS1526";
    public const string InvalidProtectedResource = "FCMS1527";

    public const string PluginDisabled = "FCMS1550";
    public const string PluginNotFound = "FCMS1551";
}
