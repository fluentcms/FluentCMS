namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageUpdatePlugin
{
    public const string FORM_NAME = "PageUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private PageSettingsModel? Model { get; set; }

    private List<LayoutDetailResponse>? Layouts { get; set; }
    private List<PageDetailResponse>? Pages { get; set; }

    private List<SelectOption>? LayoutOptions { get; set; }

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
            var pages = pagesResponse?.Data?.Where(x => !x.Locked) ?? [];

            pages = pages.Where(x => x.ParentId != Id);
            pages = pages.Where(x => x.Id != Id);

            Pages = pages.ToList();
        }

        if (Model is null)
        {
            var pageResponse = await ApiClient.Page.GetByIdAsync(Id);
            if (pageResponse.Data != null)
            {
                Model = new();
                Model.Initialize(pageResponse.Data);
            }
        }
    }

    private async Task OnSubmit()
    {
        var request = Model!.ToUpdateRequest(ViewState.Site.Id, Id);
        await ApiClient.Page.UpdateAsync(request);

        var settings = Model.ToSettingsRequest(Id);
        await ApiClient.Settings.UpdateAsync(settings);
        
        NavigateBack(true);
    }
}
