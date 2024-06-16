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
        Model.FolderId = FolderId.HasValue ? FolderId.Value : Guid.Empty;
    }

    private async Task OnSubmit()
    {
        await GetApiClient<FolderClient>().CreateAsync(Model);
        NavigateTo(GetUrl("Files List", new { folderId = FolderId }));
    }
}
// http://localhost:5000/admin/assets?
// pluginDef=File%20Management
// typeName=Files%20List
// folderId=6678c4fc-59b2-43b3-8179-78439f05fbeb