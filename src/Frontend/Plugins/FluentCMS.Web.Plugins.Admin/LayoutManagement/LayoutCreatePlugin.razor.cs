namespace FluentCMS.Web.Plugins.Admin.LayoutManagement;

public partial class LayoutCreatePlugin
{
    public const string FORM_NAME = "LayoutCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private LayoutCreateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        await Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        Model!.SiteId = ViewState.Site.Id;
        await ApiClient.Layout.CreateAsync(Model);
        NavigateBack();
    }
}
