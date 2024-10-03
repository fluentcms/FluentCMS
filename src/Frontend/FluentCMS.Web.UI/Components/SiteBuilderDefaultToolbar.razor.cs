namespace FluentCMS.Web.UI;

public partial class SiteBuilderDefaultToolbar
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    private string GetPageAddUrl()
    {
        var uri = new Uri(NavigationManager.Uri);
        var redirectTo = Uri.EscapeDataString(uri.PathAndQuery);
        var queryParams = new Dictionary<string, string?>()
        {
            { "viewType", "Create" },
            { "redirectTo", redirectTo }
        };

        var queryParamsString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

        return $"/admin/pages?{queryParamsString}";
    }

    private string GetPageEditUrl()
    {
        var uri = new Uri(NavigationManager.Uri);
        var redirectTo = Uri.EscapeDataString(uri.PathAndQuery);

        var queryParams = new Dictionary<string, string?>()
        {
            { "Id", ViewState.Page.Id.ToString() },
            { "redirectTo", redirectTo }
        };

        var queryParamsString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

        return $"/admin/pages?{queryParamsString}";
    }
}
