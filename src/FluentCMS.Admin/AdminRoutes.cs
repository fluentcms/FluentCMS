namespace FluentCMS.Admin;

// The routes the host needs: the account pages for the cookie's redirects, and the audit page with the download it posts to.
// Each page route matches the @page directive of its page.
public static class AdminRoutes
{
    public const string Setup = "/setup";
    public const string SignIn = "/sign-in";
    public const string SignOut = "/sign-out";
    public const string Audit = "/audit";
    public const string AuditExport = "/audit/export";

    // Only a path on this site is followed after sign-in, so a crafted link cannot send someone elsewhere.
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
        {
            return false;
        }

        return !url.Any(char.IsControl);
    }
}
