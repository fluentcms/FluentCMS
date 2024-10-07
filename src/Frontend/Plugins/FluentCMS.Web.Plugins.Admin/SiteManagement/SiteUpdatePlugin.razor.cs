namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteUpdatePlugin
{
    public const string FORM_NAME = "SiteUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteUpdateModel? Model { get; set; }

    private List<LayoutDetailResponse>? Layouts { get; set; }
    private SiteDetailResponse? Site { get; set; }

    protected override async Task OnInitializedAsync()
    {

        if (Model is null)
        {
            var siteResponse = await ApiClient.Site.GetByIdAsync(Id);
            Site = siteResponse.Data;
            Model = GetSiteUpdateModel(Site);
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

        var model = new SiteUpdateModel
        {
            Id = Id,
            Name = siteDetailResponse.Name ?? string.Empty,
            Description = siteDetailResponse.Description ?? string.Empty,
            LayoutId = siteDetailResponse.LayoutId,
            DetailLayoutId = siteDetailResponse.DetailLayoutId,
            EditLayoutId = siteDetailResponse.EditLayoutId,
            MetaTitle = settings["MetaTitle"] ?? string.Empty,
            MetaDescription = settings["MetaDescription"] ?? string.Empty,
            MetaKeywords = settings["MetaKeywords"] ?? string.Empty,
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
                ["MetaKeywords"] = Model.MetaKeywords
            }
        };
    }
}
