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
            Model = new SiteUpdateModel(Site);
        }

        if (Layouts is null)
        {
            var layoutsResponse = await ApiClient.Layout.GetBySiteIdAsync(Site!.Id);
            Layouts = layoutsResponse?.Data?.ToList() ?? [];
        }

    }

    private async Task OnSubmit()
    {
        Model!.Id = Id;
        await ApiClient.Site.UpdateAsync(Model.GetRequest());
        await ApiClient.Settings.UpdateAsync(Model.GetSettingsRequest());
        NavigateBack();
    }
}
