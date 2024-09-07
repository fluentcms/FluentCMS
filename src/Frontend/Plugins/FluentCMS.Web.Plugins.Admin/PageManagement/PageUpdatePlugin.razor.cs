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
    private List<SelectOption>? PageOptions { get; set; }

    private PageDetailResponse? Page { get; set; }

    private List<TreeSelectorItemType> Items = new List<TreeSelectorItemType>
        {
            new TreeSelectorItemType
            {
                Icon = "📁",
                Key = "parent-1",
                Text = "Documents",
                Items = new List<TreeSelectorItemType>
                {
                    new TreeSelectorItemType
                    {
                        Icon = "📄",
                        Key = "documents-1",
                        Text = "Project Proposal",
                        Items = new List<TreeSelectorItemType>
                        {
                            new TreeSelectorItemType { Icon = "📄", Key = "proposal-1", Text = "Version 1.0" },
                            new TreeSelectorItemType { Icon = "📄", Key = "proposal-2", Text = "Version 2.0" }
                        }
                    },
                    new TreeSelectorItemType
                    {
                        Icon = "📄",
                        Key = "documents-2",
                        Text = "Meeting Notes",
                        Items = new List<TreeSelectorItemType>
                        {
                            new TreeSelectorItemType { Icon = "📄", Key = "meeting-1", Text = "2024-09-07 Meeting" },
                            new TreeSelectorItemType { Icon = "📄", Key = "meeting-2", Text = "2024-08-30 Meeting" }
                        }
                    }
                }
            },
            new TreeSelectorItemType
            {
                Icon = "📁",
                Key = "parent-2",
                Text = "Images",
                Items = new List<TreeSelectorItemType>
                {
                    new TreeSelectorItemType
                    {
                        Icon = "🖼️",
                        Key = "images-1",
                        Text = "Vacation Photos",
                        Items = new List<TreeSelectorItemType>
                        {
                            new TreeSelectorItemType { Icon = "🖼️", Key = "vacation-1", Text = "Beach" },
                            new TreeSelectorItemType { Icon = "🖼️", Key = "vacation-2", Text = "Mountain" }
                        }
                    },
                    new TreeSelectorItemType
                    {
                        Icon = "🖼️",
                        Key = "images-2",
                        Text = "Profile Pictures",
                        Items = new List<TreeSelectorItemType>
                        {
                            new TreeSelectorItemType { Icon = "🖼️", Key = "profile-1", Text = "2024 LinkedIn" },
                            new TreeSelectorItemType { Icon = "🖼️", Key = "profile-2", Text = "2023 LinkedIn" }
                        }
                    }
                }
            },
            new TreeSelectorItemType
            {
                Icon = "📁",
                Key = "parent-3",
                Text = "Videos",
                Items = new List<TreeSelectorItemType>
                {
                    new TreeSelectorItemType
                    {
                        Icon = "📹",
                        Key = "videos-1",
                        Text = "Tutorials",
                        Items = new List<TreeSelectorItemType>
                        {
                            new TreeSelectorItemType { Icon = "📹", Key = "tutorial-1", Text = "React Basics" },
                            new TreeSelectorItemType { Icon = "📹", Key = "tutorial-2", Text = "Advanced React" }
                        }
                    },
                    new TreeSelectorItemType
                    {
                        Icon = "📹",
                        Key = "videos-2",
                        Text = "Webinars",
                        Items = new List<TreeSelectorItemType>
                        {
                            new TreeSelectorItemType { Icon = "📹", Key = "webinar-1", Text = "Tech Trends 2024" },
                            new TreeSelectorItemType { Icon = "📹", Key = "webinar-2", Text = "AI in Business" }
                        }
                    }
                }
            }
        };

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
