namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public class SiteUpdateModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public Guid LayoutId { get; set; }
    public Guid DetailLayoutId { get; set; }
    public Guid EditLayoutId { get; set; }
    public string MetaTitle { get; set; } = default!;
    public string MetaDescription { get; set; } = default!;
    public string MetaKeywords { get; set; } = default!;
    public List<string> Urls { get; set; } = [];

    public SiteUpdateModel(SiteDetailResponse siteDetailResponse)
    {
        var settings = siteDetailResponse.Settings ?? [];

        Id = siteDetailResponse.Id;
        Name = siteDetailResponse.Name ?? string.Empty;
        Description = siteDetailResponse.Description ?? string.Empty;
        LayoutId = siteDetailResponse.LayoutId;
        DetailLayoutId = siteDetailResponse.DetailLayoutId;
        EditLayoutId = siteDetailResponse.EditLayoutId;
        MetaTitle = settings!["MetaTitle"] ?? string.Empty;
        MetaDescription = settings["MetaDescription"] ?? string.Empty;
        MetaKeywords = settings["MetaKeywords"] ?? string.Empty;
        Urls = [.. siteDetailResponse.Urls];
    }

    public SiteUpdateRequest GetRequest()
    {
        return new SiteUpdateRequest
        {
            Id = Id,
            Name = Name,
            Description = Description,
            LayoutId = LayoutId,
            DetailLayoutId = DetailLayoutId,
            EditLayoutId = EditLayoutId,
            Urls = [.. Urls]
        };
    }

    public SettingsUpdateRequest GetSettingsRequest()
    {
        return new SettingsUpdateRequest
        {
            Id = Id,
            Settings = new Dictionary<string, string>
            {
                ["MetaTitle"] = MetaTitle,
                ["MetaDescription"] = MetaDescription,
                ["MetaKeywords"] = MetaKeywords
            }
        };
    }
}
