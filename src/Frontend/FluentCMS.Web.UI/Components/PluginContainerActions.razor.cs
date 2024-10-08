using AutoMapper;

namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [Inject]
    public IMapper Mapper { get; set; } = default!;

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
        if (ViewState.Type == ViewStateType.PagePreview)
        {
            redirectTo += Uri.EscapeDataString("?pagePreview=true");
        }

        queryParams.Add($"redirectTo={redirectTo}");

        var url = baseUrl;

        url += "?" + string.Join("&", queryParams);
        NavigationManager.NavigateTo(url, true);
    }

    #region Plugin Settings
    private bool SettingsModalOpen { get; set; } = false;
    private PluginSettingsModel SettingsModel { get; set; } = new();

    private async Task OpenSettingsModal()
    {
        Plugin.Settings.TryGetValue("Class", out var Class);
        Plugin.Settings.TryGetValue("Style", out var Style);

        SettingsModel = new()
        {
            Class = Class ?? "",
            Style = Style ?? "",
        };

        SettingsModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task OnSettingsClose()
    {
        SettingsModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnSettingsSubmit()
    {
        var request = new SettingsUpdateRequest
        {
            Id = Plugin.Id,
            Settings = new Dictionary<string, string>
            {
                {"Class", SettingsModel.Class},
                {"Style", SettingsModel.Style},
            }
        };
        await ApiClients.Settings.UpdateAsync(request);
        SettingsModalOpen = false;

        var pluginResponse = await ApiClients.Plugin.GetByIdAsync(Plugin.Id);
        if (pluginResponse.Data != null)
        {
            Plugin.Settings = pluginResponse.Data?.Settings ?? [];
            ViewState.StateChanged();        
        }
    }

    #endregion

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
        ViewState.Plugins.RemoveAll(x => x.Id == Plugin.Id);
        ViewState.StateChanged();
    }

    #endregion
}
