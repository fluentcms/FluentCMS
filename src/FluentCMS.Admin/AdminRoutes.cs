namespace FluentCMS.Admin;

// The routes the host needs: the account pages for the cookie's redirects, the audit page with the download it posts to, and the
// media library with its upload and file endpoints.
// Each page route matches the @page directive of its page.
public static class AdminRoutes
{
    public const string Setup = "/setup";
    public const string SignIn = "/sign-in";
    public const string SignOut = "/sign-out";
    public const string ExternalSignIn = "/sign-in/external";

    // Not a page: the host's endpoint that sends the browser to the external provider.
    public const string ExternalSignInStart = "/sign-in/external/start";

    public const string Audit = "/audit";
    public const string AuditExport = "/audit/export";

    // The media library page, and the endpoints a page cannot be: the upload its form posts to and the bytes its previews show.
    public const string Media = "/media";
    public const string MediaUpload = "/media/upload";

    public static string MediaFile(Guid id, int? version = null)
    {
        return version is { } number ? $"media/{id}/file?version={number}" : $"media/{id}/file";
    }

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
