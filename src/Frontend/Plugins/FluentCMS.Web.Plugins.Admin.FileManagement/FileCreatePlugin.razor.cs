namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileCreatePlugin
{
    public const string FORM_NAME = "FileCreateForm";

    private int MaxAllowedFiles { get; set; }
    private long MaxFileSize { get; set; }
    private string AllowedExtensions { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private string Model { get; set; } = "Model";

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    private List<FileParameter> Files { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var settingsResponse = await GetApiClient<GlobalSettingsClient>().GetAsync();
        if (settingsResponse?.Data != null)
        {
            MaxAllowedFiles = settingsResponse?.Data.FileUpload.MaxCount ?? 5;
            MaxFileSize = settingsResponse?.Data.FileUpload.MaxSize ?? (1024 * 1024);
            AllowedExtensions = settingsResponse?.Data.FileUpload.AllowedExtensions;
        }
    }

    private async Task OnFilesChanged(InputFileChangeEventArgs e)
    {
        Files = [];
        foreach (var file in e.GetMultipleFiles(MaxAllowedFiles))
        {
            var Data = file.OpenReadStream(MaxFileSize);
            Files.Add(new FileParameter(Data, file.Name, file.ContentType));
        }

        // TODO: Should Disable these lines and enable onSubmit
        await GetApiClient<FileClient>().UploadAsync(FolderId, Files);
        NavigateTo(GetUrl("Files List", new { folderId = FolderId }));
    }

    private async Task OnSubmit()
    {
        // await GetApiClient<FileClient>().UploadAsync(FolderId, Files);
        // NavigateTo(GetUrl("Files List", new { folderId = FolderId }));
    }
}