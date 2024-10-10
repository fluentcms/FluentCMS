namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteCreatePlugin
{
    public const string FORM_NAME = "SiteCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteCreateModel? Model { get; set; }
    private List<string>? Templates { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Templates is null)
        {
            var templatesResponse = await ApiClient.Setup.GetTemplatesAsync();
            Templates = templatesResponse?.Data?.ToList() ?? [];
        }

        Model ??= new();
    }

    private async Task OnSubmit()
    {
        if (Model != null)
        {
            var siteCreateRequest = GetSiteCreateRequest();
            var response = await ApiClient.Site.CreateAsync(siteCreateRequest);
            await ApiClient.Settings.UpdateAsync(ToSettingsRequest(response.Data.Id));
        }
        NavigateBack();
    }

    private SiteCreateRequest GetSiteCreateRequest()
    {
        return new SiteCreateRequest
        {
            Name = Model!.Name,
            Description = Model.Description,
            Template = Model.Template,
            Url = Model.Url
        };
    }

    private SettingsUpdateRequest ToSettingsRequest(Guid siteId)
    {
        return new SettingsUpdateRequest
        {
            Id = siteId,
            Settings = new Dictionary<string, string>
            {
                ["MetaTitle"] = Model!.MetaTitle,
                ["MetaDescription"] = Model.MetaDescription,
                ["Head"] = Model.Head,
                ["Robots"] = "index,follow",
                ["OgType"] = "website",
                ["GoogleTagsId"] = string.Empty
            }
        };
    }
}
