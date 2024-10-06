namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public class SiteCreateModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
    public string MetaKeywords { get; set; } = string.Empty;

    public SiteCreateRequest ToRequest()
    {
        return new SiteCreateRequest
        {
            Name = Name,
            Description = Description,
            Template = Template,
            Url = Url
        };
    }

    public SettingsUpdateRequest ToSettings(Guid siteId)
    {
        return new SettingsUpdateRequest
        {
            Id = siteId,
            Settings = new Dictionary<string, string>
            {
                ["MetaTitle"] = MetaTitle,
                ["MetaDescription"] = MetaDescription,
                ["MetaKeywords"] = MetaKeywords
            }
        };
    }
}
