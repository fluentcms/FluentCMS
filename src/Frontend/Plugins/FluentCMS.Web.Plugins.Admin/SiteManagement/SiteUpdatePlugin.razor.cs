namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteUpdatePlugin
{
    public const string FORM_NAME = "SiteUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteUpdateModel? Model { get; set; }

    private List<LayoutDetailResponse>? Layouts { get; set; }
    private List<RoleDetailResponse> AdminRoleOptions = [];
    private SiteDetailResponse? Site { get; set; }
    private List<SelectOption> OgTypeOptions =
    [
        new SelectOption
        {
            Title = "Website",
            Key = "website"
        },
        new SelectOption
        {
            Title = "Article",
            Key = "article"
        },
        new SelectOption
        {
            Title = "Product",
            Key = "product"
        },
        new SelectOption
        {
            Title = "Video",
            Key = "video"
        }
    ];

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var siteResponse = await ApiClient.Site.GetByIdAsync(Id);
            Site = siteResponse.Data;
            Model = GetSiteUpdateModel(Site);

            var rolesResponse = await ApiClient.Role.GetAllAsync(Site.Id);
            if (rolesResponse.Data != null)
            {
                AdminRoleOptions = (rolesResponse.Data ?? []).Where(x => x.Type != RoleTypes.AllUsers && x.Type != RoleTypes.Guest).ToList();
            }
        }


        if (Layouts is null)
        {
            var layoutsResponse = await ApiClient.Layout.GetBySiteIdAsync(Id);
            Layouts = layoutsResponse?.Data?.ToList() ?? [];
        }

    }

    private async Task OnSubmit()
    {
        await ApiClient.Site.UpdateAsync(GetSiteUpdateRequest());
        await ApiClient.Settings.UpdateAsync(GetSettingsRequest());
        NavigateBack();
    }

    private SiteUpdateModel GetSiteUpdateModel(SiteDetailResponse siteDetailResponse)
    {
        var settings = siteDetailResponse.Settings ?? [];

        settings.TryGetValue("MetaTitle", out var metaTitle);
        settings.TryGetValue("MetaDescription", out var metaDescription);
        settings.TryGetValue("GoogleTagsId", out var googleTagsId);
        settings.TryGetValue("Index", out var index);
        settings.TryGetValue("Follow", out var follow);
        settings.TryGetValue("RobotsTxt", out var robotsTxt);
        settings.TryGetValue("Theme", out var theme);
        settings.TryGetValue("OgType", out var ogType);
        settings.TryGetValue("Head", out var head);

        var model = new SiteUpdateModel
        {
            Id = Id,
            Name = siteDetailResponse.Name ?? string.Empty,
            Description = siteDetailResponse.Description ?? string.Empty,
            LayoutId = siteDetailResponse.LayoutId,
            DetailLayoutId = siteDetailResponse.DetailLayoutId,
            EditLayoutId = siteDetailResponse.EditLayoutId,
            AdminRoleIds = siteDetailResponse.AdminRoleIds!,
            ContributorRoleIds = siteDetailResponse.ContributorRoleIds!,
            MetaTitle = metaTitle ?? string.Empty,
            MetaDescription = metaDescription ?? string.Empty,
            GoogleTagsId = googleTagsId ?? string.Empty,
            Index = index == "true",
            Follow = follow == "true",
            RobotsTxt = robotsTxt ?? string.Empty,
            Theme = theme ?? string.Empty,
            OgType = ogType ?? string.Empty,
            Head = head ?? string.Empty,
            Urls = string.Join(",", siteDetailResponse.Urls ?? []),
        };
        return model;
    }

    private SiteUpdateRequest GetSiteUpdateRequest()
    {
        return new SiteUpdateRequest
        {
            Id = Id,
            Name = Model!.Name,
            Description = Model.Description,
            LayoutId = Model.LayoutId,
            DetailLayoutId = Model.DetailLayoutId,
            EditLayoutId = Model.EditLayoutId,
            AdminRoleIds = Model.AdminRoleIds,
            ContributorRoleIds = Model.ContributorRoleIds,
            Urls = [.. Model.Urls.Split(",")]
        };
    }

    private SettingsUpdateRequest GetSettingsRequest()
    {
        return new SettingsUpdateRequest
        {
            Id = Id,
            Settings = new Dictionary<string, string>
            {
                ["MetaTitle"] = Model!.MetaTitle,
                ["MetaDescription"] = Model.MetaDescription,
                ["GoogleTagsId"] = Model.GoogleTagsId,
                ["Index"] = Model.Index ? "true" : "false",
                ["Follow"] = Model.Follow ? "true" : "false",
                ["RobotsTxt"] = Model.RobotsTxt,
                ["Theme"] = Model.Theme,
                ["OgType"] = Model.OgType,
                ["Head"] = Model.Head
            }
        };
    }

    public class SelectOption
    {
        public string Title { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
    }
}
