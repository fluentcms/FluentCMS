namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFileFieldFormFilePicker
{
    #region BasePlugin
    [Inject]
    protected UserLoginResponse? UserLogin { get; set; }

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    protected TClient GetApiClient<TClient>() where TClient : class, IApiClient
    {
        return HttpClientFactory.CreateApiClient<TClient>(UserLogin);
    }
    #endregion

    private List<FileParameter> Files { get; set; }

    private async Task OnFilesChanged(InputFileChangeEventArgs e)
    {
        Files = [];
        foreach (var file in e.GetMultipleFiles(FileUploadConfig.MaxCount))
        {
            var Data = file.OpenReadStream(FileUploadConfig.MaxSize);
            Files.Add(new FileParameter(Data, file.Name, file.ContentType));
        }

        var result = await GetApiClient<FileClient>().UploadAsync(default, Files);
        if (result?.Data.Count > 0)
        {
            FieldValue.Value = result?.Data.Select(x => x.Id).ToList()[0];
            await Load();
        }
    }

    private string FileName { get; set; } = string.Empty;
    private bool TableAction { get; set; } = true;

    private FileUploadConfiguration FileUploadConfig { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var settingsResponse = await GetApiClient<GlobalSettingsClient>().GetAsync();
        if (settingsResponse?.Data != null)
        {
            FileUploadConfig = settingsResponse?.Data.FileUpload;
        }
        await Load();
    }

    private async Task Load()
    {
        if (FieldValue.Value != null)
        {
            var fileResponse = await GetApiClient<FileClient>().GetByIdAsync(FieldValue.Value.Value);
            FileName = fileResponse?.Data.Name;
        }
    }
}