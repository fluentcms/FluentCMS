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

    private List<SelectOption>? LayoutOptions { get; set; }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

    protected override async Task OnInitializedAsync()
    {
        if (LayoutOptions is null)
        {
            var layoutsResponse = await ApiClient.Layout.GetBySiteIdAsync(ViewState.Site.Id);
            var layouts = layoutsResponse?.Data?.ToList() ?? [];

            LayoutOptions = layouts.Select(x => new SelectOption
            {
                Title = x.Name!,
                Value = x.Id
            }).ToList();
        }

        await LoadPageOptions();
    }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(Model);
        await LoadPageOptions();
    }

    private async Task LoadPageOptions()
    {
        var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Id);
        var pages = pagesResponse.Data ?? [];

        PageOptions = pages.Where(x=> !x.Locked).Select(x => new SelectOption
        {
            Title = $"{x.FullPath} ({x.Title})",
            Value = x.Id
        }).OrderBy(x=> x.Title).ToList();
    }

    class SelectOption
    {
        public string Title { get; set; } = string.Empty;
        public Guid Value { get; set; }
    }
}
