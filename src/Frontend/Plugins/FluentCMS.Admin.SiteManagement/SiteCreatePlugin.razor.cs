namespace FluentCMS.Admin.SiteManagement;

public partial class SiteCreatePlugin
{
    public const string FORM_NAME = "SiteCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private SiteCreateRequest? Model { get; set; }

    private string Urls { get; set; } = string.Empty;
    private List<LayoutDetailResponse>? Layouts { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Layouts is null)
        {
            var layoutsResponse = await GetApiClient<LayoutClient>().GetAllAsync();
            Layouts = layoutsResponse?.Data?.ToList() ?? [];
        }

        Model ??= new();
    }

    private async Task OnSubmit()
    {
        Model!.Urls = Urls.Split(",");
        await GetApiClient<SiteClient>().CreateAsync(Model);
        NavigateBack();
    }
}
