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
    
    private bool FilesModalOpen { get; set; }
    private bool UploadModalOpen { get; set; }
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

    public async Task OnChooseClicked()
    {
        FilesModalOpen = true;
    }
    public async Task OnFileChoose(Guid id)
    {
        FilesModalOpen = false;

        FieldValue.Value = id;
        await Load();

        Console.WriteLine("File Chosen: " + id.ToString());
    }

    public async Task OnUploadClicked()
    {
        UploadModalOpen = true;

        Console.WriteLine("Clicked Upload button");
    }

    #region Upload File

    private async Task OnFileUpload(List<FileParameter> files)
    {
        Console.WriteLine($"OnFileUpload {files}");
        var file = await GetApiClient<FileClient>().UploadAsync(null, files);
        Console.WriteLine(file);
        if (file?.Data.Count > 0)
        {
            FieldValue.Value = file?.Data.Select(x => x.Id).ToList()[0];
            await Load();
        }

        UploadModalOpen = false;

        // await Load();
    }

    private async Task OnUploadCancel()
    {
        UploadModalOpen = false;
    }

    #endregion


}