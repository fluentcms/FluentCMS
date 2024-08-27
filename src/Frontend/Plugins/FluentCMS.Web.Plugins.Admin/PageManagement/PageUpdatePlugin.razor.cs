namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageUpdatePlugin
{
    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    public const string FORM_NAME = "PageUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private PageUpdateRequest? Model { get; set; }

    private List<LayoutDetailResponse>? Layouts { get; set; }
    private List<PageDetailResponse>? Pages { get; set; }

    private List<SelectOption>? LayoutOptions { get; set; }
    private List<SelectOption>? PageOptions { get; set; }

    private PageDetailResponse? Page { get; set; }

    async Task GetAvailableParentPages()
    {
        var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Urls[0]);
        var pages = pagesResponse?.Data?.Where(x => !x.Locked);

        pages = pages.Where(x => x.ParentId != Id);
        pages = pages.Where(x => x.Id != Id);

        Pages = pages.ToList();

        PageOptions = [
            new SelectOption
            {
                Title = "(none)",
                Value = Guid.Empty
            }
        ];

        foreach (var page in Pages)
        {
            PageOptions.Add(
                new SelectOption
                {
                    Title = page.Title,
                    Value = page.Id
                }
            );
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (Layouts is null)
        {
            var layoutsResponse = await ApiClient.Layout.GetAllAsync();
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
                        Title = layout.Name,
                        Value = layout.Id
                    }
                );
            }
        }

        if (Pages is null)
        {
            await GetAvailableParentPages();
        }

        if (Model is null)
        {
            var pageResponse = await ApiClient.Page.GetByIdAsync(Id);
            Page = pageResponse.Data;
            Model = Mapper.Map<PageUpdateRequest>(Page);
        }
    }

    private async Task OnSubmit()
    {
        if (Model.ParentId == Guid.Empty)
        {
            Model.ParentId = default!;
        }

        await ApiClient.Page.UpdateAsync(Model);
        NavigateBack();
    }
}
