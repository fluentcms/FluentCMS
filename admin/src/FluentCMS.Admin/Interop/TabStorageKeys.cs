namespace FluentCMS.Admin.Interop;

// Keys of the tab's sessionStorage that more than one area reads, so that neither depends on the other for them.
internal static class TabStorageKeys
{
    // The site the shell showed last, which a reload stays in and a sign-out clears.
    public const string Site = "fluentcms-admin/site";
}
