namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageCreatePlugin
{
    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    public const string FORM_NAME = "PageCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private PageCreateRequest? Model { get; set; }

    private string Urls { get; set; } = string.Empty;
    private List<LayoutDetailResponse>? Layouts { get; set; }
    private List<PageDetailResponse>? Pages { get; set; }

    private List<SelectOption>? LayoutOptions { get; set; }
    private List<SelectOption>? PageOptions { get; set; }

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
            var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Urls[0]);
            Pages = pagesResponse?.Data?.Where(x => !x.Locked).ToList();

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

        Model ??= new();
    }

    private async Task OnSubmit()
    {
        Model.SiteId = ViewState.Site.Id;
        await ApiClient.Page.CreateAsync(Model);
        NavigateBack();
    }
}
