namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageCreatePlugin
{
    public const string FORM_NAME = "PageCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private PageSettingsModel? Model { get; set; }

    private List<LayoutDetailResponse>? Layouts { get; set; }
    private List<PageDetailResponse>? Pages { get; set; }
    private List<SelectOption>? LayoutOptions { get; set; }
    private List<RoleViewState>? AdminRoleOptions { get; set; }

    private List<SelectOptionString> RobotsOptions =
    [
        new SelectOptionString
        {
            Title = "(default)",
            Value = string.Empty
        },
        new SelectOptionString
        {
            Title = "Index & Follow",
            Value = "index,follow"
        },
        new SelectOptionString
        {
            Title = "Index & No Follow",
            Value = "index,nofollow"
        },
        new SelectOptionString
        {
            Title = "No Index & Follow",
            Value = "noindex,follow"
        },
        new SelectOptionString
        {
            Title = "No Index & No Follow",
            Value = "noindex,nofollow"
        }
    ];

    private List<SelectOptionString> OgTypeOptions =
    [
        new SelectOptionString
        {
            Title = "(default)",
            Value = string.Empty
        },
        new SelectOptionString
        {
            Title = "Website",
            Value = "website"
        },
        new SelectOptionString
        {
            Title = "Article",
            Value = "article"
        },
        new SelectOptionString
        {
            Title = "Product",
            Value = "product"
        },
        new SelectOptionString
        {
            Title = "Video",
            Value = "video"
        }
    ];

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

            LayoutOptions.AddRange(
                Layouts.Select(layout => new SelectOption
                {
                    Title = layout.Name!,
                    Value = layout.Id
                }).ToList()
            );

        }

        AdminRoleOptions = ViewState.Site.AllRoles.Where(x => x.Type != RoleTypesViewState.AllUsers && x.Type != RoleTypesViewState.Guest).ToList();


        if (Pages is null)
        {
            var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Id);
            Pages = pagesResponse?.Data?.Where(x => !x.Locked).ToList();
        }

        Model ??= new();
    }

    private async Task OnSubmit()
    {
        var request = Model!.ToCreateRequest(ViewState.Site.Id);
        var response = await ApiClient.Page.CreateAsync(request);

        var settings = Model!.ToSettingsRequest(response.Data.Id);
        await ApiClient.Settings.UpdateAsync(settings);

        NavigateBack();
    }
}
