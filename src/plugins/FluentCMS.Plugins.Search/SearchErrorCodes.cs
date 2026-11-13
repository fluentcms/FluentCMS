namespace FluentCMS.Plugins.Search;

// The search plugin has the codes from FCMS1700 up to 1719. Each is added with AddErrorCode in SearchPlugin.
internal static class SearchErrorCodes
{
    public const string NotSearchable = "FCMS1700";
    public const string InvalidRebuild = "FCMS1701";

    // Core's code for a search without an index (spec 4.4), which a site where the plugin is disabled answers too.
    public const string NoSearchIndex = "FCMS2211";
}
