namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Parameter]
    public EventCallback OnDelete { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private bool IsDesignMode { get; set; } = false;

    private async Task HandleDelete()
    {
        await OnDelete.InvokeAsync();   
    }

    protected override async Task OnInitializedAsync()
    {
        IsDesignMode = ViewState.Type == ViewStateType.PagePreview;
        await Task.CompletedTask;
    }

    private string OpenPluginView(string viewName = "Settings") {
        var baseUrl = new Uri(NavigationManager.Uri).LocalPath;

        var queryParams = new List<string>
        {
            $"pluginId={Plugin.Id}"
        };
        
        if (!string.IsNullOrEmpty(viewName))
            queryParams.Add($"viewName={viewName}");

        var redirectTo = Uri.EscapeDataString(baseUrl + "?pagePreview=true");
        queryParams.Add($"redirectTo={redirectTo}");

        var url = baseUrl;

        url += "?" + string.Join("&", queryParams);
        return url;
    } 
}
