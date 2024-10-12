using AutoMapper;

namespace FluentCMS.Web.UI;

public partial class SiteBuilderDefaultToolbar
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IMapper Mapper { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClients { get; set; } = default!;

    private PageSettingsModel? PageSettings { get; set; }
    private bool AddPageModalOpen { get; set; } = false;
    private bool PageSettingsModalOpen { get; set; } = false;


    private async Task CloseAddPage()
    {
        AddPageModalOpen = false;
        await Task.CompletedTask;
    }
    
    private async Task ClosePageSettings()
    {
        PageSettingsModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task AddPageSubmit(PageSettingsModel model)
    {
        var request = Mapper.Map<PageCreateRequest>(model);
        request.SiteId = ViewState.Site.Id;
        
        var pageResponse = await ApiClients.Page.CreateAsync(request);
        // if (pageResponse.Data?.FullPath != null)
        // {
            AddPageModalOpen = false;
        //     NavigationManager.NavigateTo(pageResponse.Data.FullPath, true);
        // }
    }

    private async Task PageSettingsSubmit(PageSettingsModel model)
    {
        var request = Mapper.Map<PageUpdateRequest>(model);
        request.Id = ViewState.Page.Id;
        request.SiteId = ViewState.Site.Id;

        var pageResponse = await ApiClients.Page.UpdateAsync(request);
        
        // if (pageResponse.Data?.FullPath != null)
        // {
            PageSettingsModalOpen = false;
        //     NavigationManager.NavigateTo(pageResponse.Data.FullPath!, true);
        // }
    }
    
    private async Task OpenAddPage()
    {
        AddPageModalOpen = true;
        PageSettings = new();
        StateHasChanged();
        await Task.CompletedTask;
    }

    private async Task OpenPageSettings()
    {
        var pageResponse = await ApiClients.Page.GetByIdAsync(ViewState.Page.Id);

        if(pageResponse.Data != null)
        {
            PageSettings = Mapper.Map<PageSettingsModel>(pageResponse.Data);
        }
        PageSettingsModalOpen = true;
        await Task.CompletedTask;
    }
    protected override async Task OnInitializedAsync()
    { 
    
    }
}
