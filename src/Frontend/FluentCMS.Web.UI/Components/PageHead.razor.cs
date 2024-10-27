namespace FluentCMS.Web.UI;

public partial class PageHead
{
    [Inject]
    private ViewState ViewState { get; set; } = default!;

    private string GetSetting(string key)
    {
        ViewState.Page.Settings.TryGetValue(key, out var pageValue);
        ViewState.Site.Settings.TryGetValue(key, out var siteValue);

        return pageValue ?? siteValue ?? string.Empty;
    }

    private string GetGoogleTagsScript()
    {
        return $"<script async src=\"https://www.googletagmanager.com/gtag/js?id={GetSetting("GoogleTagsId")}\"></script>\n<script>\n\twindow.dataLayer = window.dataLayer || [];\n\tfunction gtag(){{\n\t\tdataLayer.push(arguments);\n\t}}\ngtag('js', new Date())\ngtag('config', '{GetSetting("GoogleTagsId")}');\n</script>";
    }
}