using AutoMapper;

namespace FluentCMS.Web.UI;

public partial class PageSettingsModal
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    private IMapper Mapper { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    [Parameter]
    public bool Open { get; set; } = false;

    [Parameter]
    public string Title { get; set; } = "Page Settings";

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public EventCallback<PageDetailResponse> OnSubmit { get; set; }

    [Parameter]
    public Guid? Id { get; set; } = default!;

    private List<SelectOption> PageOptions { get; set; } = [];

    private List<SelectOption>? LayoutOptions { get; set; }

    private PageSettingsModel? Model { get; set; }


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

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

    protected override async Task OnInitializedAsync()
    {
        if (Id is null)
        {
            Model = new();
        }
        else
        {
            var pageResponse = await ApiClient.Page.GetByIdAsync(Id.Value);
            var settings = pageResponse.Data.Settings ?? [];

            if (pageResponse.Data != null)
            {
                Model = Mapper.Map<PageSettingsModel>(pageResponse.Data);

                settings.TryGetValue("MetaTitle", out var metaTitle);
                settings.TryGetValue("MetaDescription", out var metaDescription);
                settings.TryGetValue("OgType", out var ogType);
                settings.TryGetValue("Robots", out var robots);
                settings.TryGetValue("Head", out var head);

                Model.MetaTitle = metaTitle ?? string.Empty;
                Model.MetaDescription = metaDescription ?? string.Empty;
                Model.OgType = ogType ?? string.Empty;
                Model.Robots = robots ?? string.Empty;
                Model.Head = head ?? string.Empty;
            }
        }

        if (LayoutOptions is null)
        {
            var layoutsResponse = await ApiClient.Layout.GetBySiteIdAsync(ViewState.Site.Id);
            var layouts = layoutsResponse?.Data?.ToList() ?? [];

            LayoutOptions = [
                new SelectOption
                {
                    Title = "(default)",
                    Value = Guid.Empty
                }
            ];

            LayoutOptions.AddRange(
                layouts.Select(x => new SelectOption
                {
                    Title = x.Name!,
                    Value = x.Id
                }).ToList()
            );
        }

        await LoadPageOptions();
    }

    private async Task HandleSubmit()
    {
        PageDetailResponse response;

        if(Model!.ParentId == Guid.Empty)
            Model.ParentId = default!;
            
        if(Model!.LayoutId == Guid.Empty)
            Model.LayoutId = default!;

        if(Model!.EditLayoutId == Guid.Empty)
            Model.EditLayoutId = default!;

        if(Model!.DetailLayoutId == Guid.Empty)
            Model.DetailLayoutId = default!;

        if (Id is null)
        {
            var request = Model!.ToCreateRequest(ViewState.Site.Id);
            var pageResponse = await ApiClient.Page.CreateAsync(request);

            var settings = Model!.ToSettingsRequest(pageResponse.Data.Id);
            await ApiClient.Settings.UpdateAsync(settings);

            response = pageResponse.Data;
        }
        else
        {
            var request = Model!.ToUpdateRequest(ViewState.Site.Id, Id.Value);
            var pageResponse = await ApiClient.Page.UpdateAsync(request);

            var settings = Model!.ToSettingsRequest(Id.Value);
            await ApiClient.Settings.UpdateAsync(settings);
            
            response = pageResponse.Data;
        }

        await OnSubmit.InvokeAsync(response);
        await LoadPageOptions();
    }

    private async Task LoadPageOptions()
    {
        var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Id);
        var pages = pagesResponse.Data ?? [];

        PageOptions = [
            new SelectOption
            {
                Title = "(default)",
                Value = Guid.Empty
            }
        ];

        PageOptions.AddRange(
            pages.Where(x=> {
                if(x.Locked) 
                    return false;
                
                // For add modal, show all pages in select.
                if(Id is null)
                    return true;

                if((x.FullPath + "/")!.StartsWith(ViewState.Page.FullPath + "/"))
                {
                    return false;
                }

                return true;
            }).Select(x => new SelectOption
            {
                Title = $"{x.FullPath} ({x.Title})",
                Value = x.Id
            }).OrderBy(x=> x.Title).ToList()
        );
    }

    class SelectOption
    {
        public string Title { get; set; } = string.Empty;
        public Guid? Value { get; set; }
    }

    class SelectOptionString
    {
        public string Title { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

}
