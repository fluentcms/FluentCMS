namespace FluentCMS.Admin.Content;

// The admin's routes of content (spec 3.2).
internal static class ContentRoutes
{
    // The history state of a navigation that keeps focus where it is, such as the move of a new entry to its route.
    public const string KeepFocus = "fluentcms-admin/keep-focus";

    // Where the Management API's paths start, as a create's Location writes them.
    public const string ManagementPrefix = "api/management/v1/";

    public static string Type(string type) => $"content/{Uri.EscapeDataString(type)}";

    public static string New(string type) => $"{Type(type)}/new";

    public static string Entry(string type, Guid id) => $"{Type(type)}/{id:D}";
}
