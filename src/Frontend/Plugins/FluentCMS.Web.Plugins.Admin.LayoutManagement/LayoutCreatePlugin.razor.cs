namespace FluentCMS.Web.Plugins.Admin.LayoutManagement;

public partial class LayoutCreatePlugin
{
    public const string FORM_NAME = "LayoutCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private LayoutCreateRequest? Model { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        Model ??= new();
        await Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        await GetApiClient<LayoutClient>().CreateAsync(Model);
        NavigateBack();
    }
}
