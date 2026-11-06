namespace FluentCMS.Core.Plugins;

// 1520 to 1599 are kept for later plugin work.
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
}
