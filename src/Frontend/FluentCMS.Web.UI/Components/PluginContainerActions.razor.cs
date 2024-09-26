namespace FluentCMS.Web.UI;

public partial class PluginContainerActions: IDisposable
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClients { get; set; } = default!;

    private void OpenPluginView(string viewName = "Settings")
    {
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

    #region Delete Plugin

    private bool DeleteConfirmModalOpen { get; set; } = false;

    private async Task OpenDeleteConfirm()
    {
        DeleteConfirmModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task OnConfirmClose()
    {
        DeleteConfirmModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnDeleteConfirm()
    {
        DeleteConfirmModalOpen = false;
        await ApiClients.Plugin.DeleteAsync(Plugin.Id);
        ViewState.PluginRemoved(Plugin.Id);
    }

    #endregion
}
