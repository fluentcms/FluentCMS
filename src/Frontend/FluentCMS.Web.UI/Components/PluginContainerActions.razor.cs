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
        var authenticated = ViewState.Type == ViewStateType.Default && !ViewState.Page.Locked && ViewState.User.Roles.Any(role => role.Type == RoleTypesViewState.Authenticated);
        IsDesignMode = authenticated || ViewState.Type == ViewStateType.PagePreview;
        await Task.CompletedTask;
    }

    private void OpenPluginView(string viewName = "Settings") {
        var baseUrl = new Uri(NavigationManager.Uri).LocalPath;

        var queryParams = new List<string>
        {
            $"pluginId={Plugin.Id}"
        };
        
        if (!string.IsNullOrEmpty(viewName))
            queryParams.Add($"viewName={viewName}");

        var redirectTo = Uri.EscapeDataString(baseUrl);
        queryParams.Add($"redirectTo={redirectTo}");

        var url = baseUrl;

        url += "?" + string.Join("&", queryParams);
        NavigationManager.NavigateTo(url, true);
    } 
}
