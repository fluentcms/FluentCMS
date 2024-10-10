namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageCreatePlugin
{
    public const string FORM_NAME = "PageCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private PageCreateRequest? Model { get; set; }

    [SupplyParameterFromQuery(Name = "openNewPage")]
    private bool OpenNewPage { get; set; } = false;

    private List<LayoutDetailResponse>? Layouts { get; set; }
    private List<PageDetailResponse>? Pages { get; set; }
    private List<SelectOption>? LayoutOptions { get; set; }

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

        if (Pages is null)
        {
            var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Id);
            Pages = pagesResponse?.Data?.Where(x => !x.Locked).ToList();
        }

        Model ??= new();
    }

    private static string GetFullPath(Dictionary<Guid, PageDetailResponse> allPages, PageDetailResponse page)
    {
        var result = new List<string>();
        var currentPage = page;
        while (currentPage != null)
        {
            result.Add(currentPage.Path!);
            currentPage = currentPage.ParentId.HasValue ? allPages[currentPage.ParentId.Value] : default!;
        }
        result.Reverse();

        return string.Join("", result);
    }

    private async Task OnSubmit()
    {
        Model!.SiteId = ViewState.Site.Id;
        if (Model.ParentId == Guid.Empty)
            Model.ParentId = default!;

        if (Model.LayoutId == Guid.Empty)
            Model.LayoutId = default!;

        var response = await ApiClient.Page.CreateAsync(Model);

        if (OpenNewPage)
        {
            var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Id);
            var pagesDictionary = pagesResponse!.Data!.ToDictionary(x => x.Id, x => x);

            var path = GetFullPath(pagesDictionary, response.Data!);
            NavigateTo(path);
        }
        else
        {
            NavigateBack();
        }
    }
}
