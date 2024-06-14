namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderCreatePlugin
{
    public const string FORM_NAME = "FolderCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private FolderCreateRequest Model { get; set; } = new();

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model.FolderId is null && FolderId != null)
        {
            Model.FolderId = FolderId;
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<FolderClient>().CreateAsync(Model);
        NavigateBack();
    }
}