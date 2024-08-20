namespace FluentCMS.Web.Plugins;

public partial class FormActions
{
    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool Submit { get; set; }

    [Parameter]
    public bool Cancel { get; set; }

    protected virtual string GetBackUrl()
    {
        var uri = new Uri(NavigationManager.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var redirectTo = query["redirectTo"];

        if (!string.IsNullOrEmpty(redirectTo))
        {
            return Uri.UnescapeDataString(redirectTo);
        }
        else
        {
            return uri.LocalPath;
        }
    }
}
