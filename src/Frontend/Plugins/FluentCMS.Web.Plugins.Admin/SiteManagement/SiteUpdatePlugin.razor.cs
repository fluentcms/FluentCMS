namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteUpdatePlugin
{
    public const string FORM_NAME = "SiteUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteUpdateRequest? Model { get; set; }

    private string Urls { get; set; } = string.Empty;
    private List<LayoutDetailResponse>? Layouts { get; set; }

    private SiteDetailResponse? Site { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Layouts is null)
        {
            var layoutsResponse = await ApiClient.Layout.GetBySiteIdAsync(ViewState.Site.Id);
            Layouts = layoutsResponse?.Data?.ToList() ?? [];
        }

        if (Model is null)
        {
            var siteResponse = await ApiClient.Site.GetByIdAsync(Id);
            Site = siteResponse.Data;
            Model = Mapper.Map<SiteUpdateRequest>(Site);
            Urls = string.Join(",", Model.Urls ?? []);
        }

    }

    private async Task OnSubmit()
    {
        Model!.Urls = Urls.Split(",");
        await ApiClient.Site.UpdateAsync(Model);
        NavigateBack();
    }
}
