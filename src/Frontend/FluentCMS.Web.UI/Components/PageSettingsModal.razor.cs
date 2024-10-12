namespace FluentCMS.Web.UI;

public partial class PageSettingsModal
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;
    
    [Inject]
    private ViewState ViewState { get; set; } = default!;
    
    [Parameter]
    public bool Open { get; set; } = false;

    [Parameter]
    public string Title { get; set; } = "Page Settings";

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public EventCallback<PageSettingsModel> OnSubmit { get; set; }

    [Parameter]
    public PageSettingsModel? Model { get; set; }

    private List<SelectOption> PageOptions { get; set; } = [];

    private List<LayoutDetailResponse> Layouts { get; set; } = default!;

    private List<SelectOption>? LayoutOptions { get; set; }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(Model);
        await LoadPageOptions();
    }

    private async Task LoadPageOptions()
    {
        var pagesResponse = await ApiClient.Page.GetHierarchyBySiteIdAsync(ViewState.Site.Id);
        var pages = pagesResponse.Data ?? [];

        PageOptions = [];

        PageOptions.Add(
            new SelectOption
            {
                Title = "(none)",
                Value = default!,
            }
        );

        await AddPageOptions(pages);
    }

    private async Task AddPageOptions(ICollection<PageDetailResponse> pages, string prefix = "")
    {
        foreach (var page in pages)
        {
            if ((Model?.Id != null && ViewState.Page.Id == page.Id) || page.Locked)
                continue;

            PageOptions.Add( 
                new SelectOption{
                    Title = prefix + " / " + page.Title!,
                    Value = page.Id
                }
            );

            if (page.Children != null && page.Children.Count > 0)
            {
                await AddPageOptions(page.Children, prefix + " / " + page.Title);
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (Layouts is null)
        {
            var layoutsResponse = await ApiClient.Layout.GetBySiteIdAsync(ViewState.Site.Id);
            Layouts = layoutsResponse?.Data?.ToList() ?? [];

            LayoutOptions = [
                new SelectOption
                {
                    Title = "(default)",
                    Value = Guid.Empty
                }
            ];

            foreach (var layout in Layouts)
            {
                LayoutOptions.Add(
                    new SelectOption
                    {
                        Title = layout.Name!,
                        Value = layout.Id
                    }
                );
            }
        }

        await LoadPageOptions();
    }

    class SelectOption
    {
        public string Title { get; set; } = string.Empty;
        public Guid Value { get; set; }
    }
}