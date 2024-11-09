namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFileFieldFormFilePicker
{
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    private List<FileParameter> Files { get; set; } = [];

    private async Task OnFilesChanged(InputFileChangeEventArgs e)
    {
        Files = [];
        foreach (var file in e.GetMultipleFiles(FileUploadConfig!.MaxCount))
        {
            var Data = file.OpenReadStream(FileUploadConfig!.MaxSize);
            Files.Add(new FileParameter(Data, file.Name, file.ContentType));
        }

        // TODO upload file in root folder or other folder.
        var result = await ApiClient.File.UploadAsync(default, Files);
        if (result?.Data != null && result.Data.Count > 0)
        {
            FieldValue.Value = result.Data.Select(x => x.Id).ToList()[0];
            await Load();
        }
    }

    private string FileName { get; set; } = string.Empty;
    private bool TableAction { get; set; } = true;

    private FileUploadConfig? FileUploadConfig { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var settingsResponse = await ApiClient.GlobalSettings.GetAsync();
        if (settingsResponse?.Data != null)
        {
            FileUploadConfig = settingsResponse.Data.FileUpload;
        }
        await Load();
    }

    private async Task Load()
    {
        if (FieldValue.Value != null)
        {
            var fileResponse = await ApiClient.File.GetByIdAsync(FieldValue.Value.Value);

            if (fileResponse.Data != null)
                FileName = fileResponse?.Data.Name ?? string.Empty;
        }
    }
}
