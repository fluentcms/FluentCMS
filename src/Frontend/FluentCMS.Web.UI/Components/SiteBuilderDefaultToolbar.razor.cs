using AutoMapper;

namespace FluentCMS.Web.UI;

public partial class SiteBuilderDefaultToolbar
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    #region Add Page

    private bool AddPageModalOpen { get; set; } = false;

    private async Task OpenAddPage()
    {
        AddPageModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task CloseAddPage()
    {
        AddPageModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task AddPageSubmit(PageDetailResponse response)
    {
        if (response.FullPath != null)
        {
            AddPageModalOpen = false;
            NavigationManager.NavigateTo(response.FullPath, true);
        }
        await Task.CompletedTask;
    }

    #endregion

    #region Page Settings

    private bool PageSettingsModalOpen { get; set; } = false;

    private async Task OpenPageSettings()
    {
        PageSettingsModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task ClosePageSettings()
    {
        PageSettingsModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task PageSettingsSubmit(PageDetailResponse response)
    {
        PageSettingsModalOpen = false;
        if (response.FullPath != null)
        {
            NavigationManager.NavigateTo(response.FullPath!, true);
        }
        await Task.CompletedTask;
    }

    #endregion
}
