namespace FluentCMS.Web.Plugins.Admin.BlockManagement;

public partial class BlockUpdatePlugin
{
    public const string FORM_NAME = "BlockUpdateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private BlockUpdateRequest? Model { get; set; }

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var blockResponse = await ApiClient.Block.GetByIdAsync(Id);
            var block = blockResponse.Data;
            Model = Mapper.Map<BlockUpdateRequest>(block);
        }
    }

    private async Task OnSubmit()
    {
        await ApiClient.Block.UpdateAsync(Model);
        NavigateBack();
    }
}
