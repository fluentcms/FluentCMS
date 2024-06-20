namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFilePickerModal
{

public abstract class BaseContentTypeFieldComponent : BaseComponent
{
    #region Base Plugin
    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Inject]
    protected UserLoginResponse? UserLogin { get; set; }

    protected T GetApiClient<T>()
    {
        return HttpClientFactory.CreateApiClient<T>(UserLogin);
    }

    #endregion

    [Parameter]
    public Guid Model { get; set; }

    [Parameter]
    public EventCallback<Guid> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private Guid CurrentFolderId { get; set; } = Guid.Empty;

    private bool FileUploadModalOpen { get; set; }
    private FolderDetailResponse RootFolder { get; set; }

    public async Task OnUploadClicked()
    {
        FileUploadModalOpen = true;
        Console.WriteLine("Open local File Picker");
    }


    private async Task Load()
    {
        var rootFolderResponse = await GetApiClient<FolderClient>().GetAllAsync();
        RootFolder = rootFolderResponse?.Data;

        FolderDetailResponse? folderDetail = default!;

        var folderDetailResponse = await GetApiClient<FolderClient>().GetAllAsync();

        if (CurrentFolderId is null || CurrentFolderId == Guid.Empty)
        {
            folderDetail = folderDetailResponse?.Data;
        }
        else
        {
            folderDetail = FindFolderById(folderDetailResponse?.Data?.Folders, CurrentFolderId.Value);
        }

        if (folderDetail != null)
        {
            ParentId = folderDetail.FolderId;

            Items = [];

            if (CurrentFolderId != null && CurrentFolderId != Guid.Empty)
            {
                Items.Add(new AssetDetail
                {
                    Name = "(parent)",
                    IsFolder = true,
                    Id = folderDetail.FolderId == Guid.Empty ? null : folderDetail.FolderId,
                    IsParentFolder = true
                });
            }

            foreach (var item in folderDetail.Folders)
            {
                Items.Add(new AssetDetail
                {
                    Name = item.Name,
                    IsFolder = true,
                    Id = item.Id,
                    FolderId = item.FolderId,
                    Size = item.Size,
                });
            }

            foreach (var item in folderDetail.Files)
            {
                Items.Add(new AssetDetail
                {
                    Name = item.Name,
                    IsFolder = false,
                    FolderId = item.FolderId,
                    Id = item.Id,
                    Size = item.Size,
                    ContentType = item.ContentType
                });
            }
        }
    }

    private async Task LoadFiles()
    {
        // CurrentFolderId

    }

    private async Task OnUpload(List<FileParameter> files)
    {
        await GetApiClient<FileClient>().UploadAsync(CurrentFolderId, files);
        FileUploadModalOpen = false;
        await Load();
    }

    public async Task OnUpload()
    {
        FileUploadModalOpen = true;
        Console.WriteLine("Open local File Picker");
    }

    public async Task OnUploadCancel()
    {
        FileUploadModalOpen = false;
    }

    private async Task HandleSubmit()
    {
        OnSubmit.InvokeAsync(Model);
    }

    private async Task HandleCancel()
    {
        OnCancel.InvokeAsync();
    }
}