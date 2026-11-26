namespace FluentCMS.Admin.Interop;

// The tab's sessionStorage: what the sign-in keeps across the redirects of one tab and nothing beyond it (AQ3). The browser
// clears it when the tab closes, and another tab never sees it.
internal interface ISessionStorage
{
    ValueTask<string?> GetAsync(string key);

    ValueTask SetAsync(string key, string value);

    ValueTask RemoveAsync(string key);
}

// The browser refused the tab's sessionStorage, as some privacy settings do: the admin cannot sign in without it (spec 2.4).
internal sealed class TabStorageRefusedException(Exception innerException)
    : Exception("The browser refused the tab's sessionStorage.", innerException);
