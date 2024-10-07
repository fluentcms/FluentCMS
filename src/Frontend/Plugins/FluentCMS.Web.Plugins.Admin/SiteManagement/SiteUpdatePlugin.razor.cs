namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteUpdatePlugin
{
    public const string FORM_NAME = "SiteUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteUpdateModel? Model { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private string Urls { get; set; } = string.Empty;
    private List<LayoutDetailResponse>? Layouts { get; set; }

    private SiteDetailResponse? Site { get; set; }

    protected override async Task OnInitializedAsync()
    {

        if (Model is null)
        {
            var siteResponse = await ApiClient.Site.GetByIdAsync(Id);
            Site = siteResponse.Data;
            Model = new SiteUpdateModel(Site);
            Urls = string.Join(",", Model.Urls ?? []);
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
        Model!.Urls = [.. Urls.Split(",")];
        await ApiClient.Site.UpdateAsync(Model.GetRequest());
        await ApiClient.Settings.UpdateAsync(Model.GetSettingsRequest());
        NavigateBack();
    }
}
