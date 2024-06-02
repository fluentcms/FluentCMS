namespace FluentCMS.Web.UI.Plugins.SiteManagement;

public partial class SiteCreatePlugin
{
    public const string FORM_NAME = "SiteCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteCreateRequest Model { get; set; } = new();

    private string Urls { get; set; }
    private List<LayoutDetailResponse> Layouts { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var layoutsResponse = await GetApiClient<LayoutClient>().GetAllAsync();
        Layouts = layoutsResponse?.Data?.ToList() ?? [];

        Urls = "";

        Model.Languages = [];
        Model.Urls = [];
        Model.DefaultPageTitle = "";
    }

    private async Task OnSubmit()
    {
        Model.Urls = Urls.Split("\n");
        await GetApiClient<SiteClient>().CreateAsync(Model);
        NavigateBack();
    }
}
