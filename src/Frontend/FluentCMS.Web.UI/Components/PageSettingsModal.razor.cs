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

    private List<string> Errors { get; set; } = [];

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

            if (pageResponse.Data != null)
            {
                Model = new();
                Model.Initialize(pageResponse.Data);
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

        try
        {
            PageDetailResponse response;

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
        }
        catch (ApiClientException ex)
        {
            Errors = ex.ApiResult?.Errors?.Select(x => $"{x.Code ?? string.Empty}: {x.Description ?? string.Empty}").ToList() ?? [ex.Message];
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Errors = [ex.Message];
            StateHasChanged();
        }
        
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
        public Guid Value { get; set; }
    }

    class SelectOptionString
    {
        public string Title { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

}
