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
            var sireCreateRequest = new SiteCreateRequest
            {
                Name = Model.Name,
                Description = Model.Description,
                Template = Model.Template,
                Url = Model.Url,
                Settings = new Dictionary<string, string>
                {
                    ["MetaTitle"] = Model.MetaTitle,
                    ["MetaDescription"] = Model.MetaDescription,
                    ["MetaKeywords"] = Model.MetaKeywords,
                    ["FaviconUrl"] = Model.FaviconUrl
                }
            };
            await ApiClient.Site.CreateAsync(sireCreateRequest);
        }
        NavigateBack();
    }
}

public class SiteCreateModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
    public string MetaKeywords { get; set; } = string.Empty;
    public string FaviconUrl { get; set; } = string.Empty;
}
