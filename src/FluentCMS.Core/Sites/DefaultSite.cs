namespace FluentCMS.Core.Sites;

// The site that the system migration seeds, with fixed ids so that every instance and every export agrees on them.
public static class DefaultSite
{
    public const string Name = "Default";
    public const string Slug = "default";
    public const string Locale = "en";

    public static readonly Guid Id = new("00000000-0000-7000-8002-000000000001");

    // The default site's own Public role, beside the instance-wide BuiltInRoles.Public.
    public static readonly Guid PublicRole = new("00000000-0000-7000-8000-000000000006");
}
