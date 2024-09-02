namespace FluentCMS.Web.Plugins.Admin.BlockManagement;

public partial class BlockCreatePlugin
{
    public const string FORM_NAME = "BlockCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private BlockCreateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        await Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        await ApiClient.Block.CreateAsync(Model);
        NavigateBack();
    }
}
