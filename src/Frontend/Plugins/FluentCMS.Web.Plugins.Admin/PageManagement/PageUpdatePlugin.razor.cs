namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageUpdatePlugin
{
    public const string FORM_NAME = "PageUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private PageUpdateRequest? Model { get; set; }

    private List<LayoutDetailResponse>? Layouts { get; set; }
    private List<PageDetailResponse>? Pages { get; set; }

    private List<SelectOption>? LayoutOptions { get; set; }

    private PageDetailResponse? Page { get; set; }

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
                        Title = layout.Name ?? string.Empty,
                        Value = layout.Id
                    }
                );
            }
        }

        if (Pages is null)
        {
            var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Urls[0]);
            var pages = pagesResponse?.Data?.Where(x => !x.Locked) ?? [];

            pages = pages.Where(x => x.ParentId != Id);
            pages = pages.Where(x => x.Id != Id);

            Pages = pages.ToList();
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
        if (Model!.ParentId == Guid.Empty)
            Model.ParentId = default!;

        if (Model.LayoutId == Guid.Empty)
            Model.LayoutId = default!;

        await ApiClient.Page.UpdateAsync(Model);
        NavigateBack();
    }
}
