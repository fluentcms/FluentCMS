namespace FluentCMS.Web.UI.Plugins.SiteManagement;

public partial class SiteUpdatePlugin
{
    public const string FORM_NAME = "SiteUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteUpdateRequest Model { get; set; } = new();

    private string Urls { get; set; }
    private List<LayoutDetailResponse> Layouts { get; set; }

    private SiteDetailResponse? Site { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var layoutsResponse = await GetApiClient<LayoutClient>().GetAllAsync();
        Layouts = layoutsResponse?.Data?.ToList() ?? [];

        if (Site is null)
        {
            var siteResponse = await GetApiClient<SiteClient>().GetByIdAsync(Id);
            Site = siteResponse.Data;
            Model = Mapper.Map<SiteUpdateRequest>(Site);
            Model.Languages = [];
            Model.DefaultPageTitle ??= "";
            Urls = String.Join("\n", Model.Urls);
        }

    }

    private async Task OnSubmit()
    {
        Model.Urls = Urls.Split("\n");
        await GetApiClient<SiteClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
