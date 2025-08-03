using System.Diagnostics.CodeAnalysis;
namespace FluentCMS.Web.Plugins;

public partial class FormRichTextEditor : IAsyncDisposable
{
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    public ElementReference? Element { get; set; }

    private IJSObjectReference? Module { get; set; }
    private DotNetObjectReference<FormRichTextEditor>? DotNetRef { get; set; }

    private List<AssetDetail> Assets { get; set; } = [];
    private List<PageDetailResponse> Pages { get; set; } = [];
    private bool ShowClearButton { get; set; } = true;
    private bool LinkModalOpen { get; set; } = false;
    private bool ImageModalOpen { get; set; } = false;
    private string ImageMode { get; set; } = "Library";
    private string? ImageUrl { get; set; }
    private Guid? ImageId { get; set; }
    private Guid? FolderId { get; set; }
    private string? ImageAlt { get; set; }
    private string? Href { get; set; }
    private string? Text { get; set; }
    private string Mode { get; set; } = "Page";

    private FileUploadConfig? FileUploadConfig { get; set; }

    [Parameter]
    public int Cols { get; set; } = 12;

    private string? _value;

    [JSInvokable]
    public async Task UpdateValue(string value)
    {
        _value = value;
        await ValueChanged.InvokeAsync(value);
    }

    public class OpenLinkParams
    {
        public string Mode { get; set; } = default!;
        public string Text { get; set; } = default!;
        public string Href { get; set; } = default!;
    }

    public class OpenImageParams
    {
        public string Mode { get; set; } = default!;
        public Guid? Id { get; set; } = default!;
        public string? Url { get; set; } = default!;
        public string? Alt { get; set; } = default!;
    }


    [JSInvokable]
    public async Task OpenLinkModal(OpenLinkParams value)
    {
        Text = value.Text ?? "";
        Mode = value.Mode ?? "External";
        Href = value.Href ?? "";

        if (string.IsNullOrEmpty(value.Href))
        {
            ShowClearButton = false;
        }
        else
        {
            ShowClearButton = true;
        }
        LinkModalOpen = true;
        StateHasChanged();

        await Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OpenImageModal(OpenImageParams value)
    {
        ImageMode = value.Mode;
        ImageUrl = value.Url ?? "";
        ImageAlt = value.Alt ?? "";
        // ImageId = value.Id ?? "";

        ImageModalOpen = true;
        StateHasChanged();

        await Task.CompletedTask;
    }

    private async Task OnLinkClear()
    {
        LinkModalOpen = false;

        if (Module != null)
            await Module.InvokeVoidAsync("setLink", DotNetRef, Element, new { Mode = "Clear" });

    }

    private async Task OnLinkModalClose()
    {
        LinkModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnChooseExternal()
    {

        LinkModalOpen = false;

        if (Module != null)
            await Module.InvokeVoidAsync("setLink", DotNetRef, Element, new { Href, Text, Mode = "External" });

    }

    private async Task OnChooseFile(AssetDetail file)
    {
        Text = file.Path?.Split("/").Last();
        Href = file.Path;

        LinkModalOpen = false;

        if (Module != null)
            await Module.InvokeVoidAsync("setLink", DotNetRef, Element, new { Href, Text, Mode = "File" });

    }

    string GetPageUrl(Guid? pageId)
    {
        var page = Pages.Where(x => x.Id == pageId).FirstOrDefault();

        if (page != null)
        {
            if (page.ParentId != null)
            {
                return GetPageUrl(page.ParentId) + page.Path;
            }
            return page?.Path ?? "";
        }
        else
        {
            return "";
        }
    }

    private async Task OnChoosePage(PageDetailResponse page)
    {
        Text = page.Title;
        Href = GetPageUrl(page.Id);

        LinkModalOpen = false;

        if (Module != null)
            await Module.InvokeVoidAsync("setLink", DotNetRef, Element, new { Href = Href, Text = Text, Mode = "Page" });

    }

    protected override async Task OnInitializedAsync()
    {
        var settingsResponse = await ApiClient.GlobalSettings.GetAsync();
        if (settingsResponse?.Data != null)
        {
            FileUploadConfig = settingsResponse?.Data.FileUpload;
        }

        // TODO: Site url
        //var pagesResponse = await ApiClient.Page.GetAllAsync("localhost:5000");
        //if (pagesResponse?.Data != null)
        //{
        //    Pages = pagesResponse.Data.ToList();
        //}
        Pages = [];

        await OnNavigateFolder(null);
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (Value == _value) return;

        _value = Value;

        if (Module != null)
            await Module.InvokeVoidAsync("update", DotNetRef, Element, new { Value });
    }

    FolderDetailResponse? FindFolderById(ICollection<FolderDetailResponse> folders, Guid folderId)
    {
        foreach (var folder in folders)
        {
            if (folder.Id == folderId)
                return folder;

            if (folder.Folders != null && folder.Folders.Count > 0)
            {
                var foundFolder = FindFolderById(folder.Folders, folderId);
                if (foundFolder != null)
                    return foundFolder;
            }
        }
        return null;
    }

    private async Task OnNavigateFolder(Guid? folderId)
    {
        FolderId = folderId;
        FolderDetailResponse? folderDetail;

        var folderDetailResponse = await ApiClient.Folder.GetAllAsync(ViewState.Site.Id);

        if (folderId is null || folderId == Guid.Empty)
        {
            folderDetail = folderDetailResponse?.Data;
        }
        else
        {
            folderDetail = FindFolderById(folderDetailResponse?.Data?.Folders ?? [], folderId.Value);
        }

        if (folderDetail != null)
        {
            Assets = [];

            if (folderId != null && folderId != Guid.Empty)
            {
                Assets.Add(new AssetDetail
                {
                    Name = "(parent)",
                    IsFolder = true,
                    Id = folderDetail.Id,
                    IsParentFolder = true
                });
            }

            foreach (var item in folderDetail.Folders ?? [])
            {
                Assets.Add(new AssetDetail
                {
                    Name = item.Name ?? string.Empty,
                    IsFolder = true,
                    Id = item.Id,
                    ParentId = item.ParentId,
                    Size = item.Size,
                });
            }

            foreach (var item in folderDetail.Files ?? [])
            {
                Assets.Add(new AssetDetail
                {
                    Name = item.Name ?? string.Empty,
                    IsFolder = false,
                    ParentId = item.FolderId,
                    Id = item.Id,
                    Size = item.Size,
                    ContentType = item.ContentType ?? string.Empty
                });
            }
        }
        StateHasChanged();
    }

    public async Task OnChooseImage(AssetDetail file)
    {
        var url = file.Path;
        ImageModalOpen = false;
        ImageAlt = "(TODO) Image ALT";

        if (Module != null)
            await Module.InvokeVoidAsync("setImage", DotNetRef, Element, new { Alt = ImageAlt, Url = url });
    }

    public async Task OnUpload()
    {
        if (Module != null)
            await Module.InvokeVoidAsync("openFileUpload", DotNetRef, Element, new { Id = "upload-" + Id });
    }


    private async Task OnFilesChanged(InputFileChangeEventArgs e)
    {
        if (FileUploadConfig != null)
        {
            List<FileParameter> files = [];
            foreach (var file in e.GetMultipleFiles(FileUploadConfig.MaxCount))
            {
                var Data = file.OpenReadStream(FileUploadConfig.MaxSize);
                files.Add(new FileParameter(Data, file.Name, file.ContentType));
            }
            await ApiClient.File.UploadAsync(FolderId, files);
            await OnNavigateFolder(FolderId);
        }
    }

    public async Task OnImageModalClose()
    {
        ImageModalOpen = false;
        await Task.CompletedTask;
    }

    public async Task OnChooseImageExternal()
    {
        ImageModalOpen = false;

        if (Module != null)
            await Module.InvokeVoidAsync("setImage", DotNetRef, Element, new { Alt = ImageAlt, Url = ImageUrl });

    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Module is not null)
            {
                await Module.InvokeVoidAsync("dispose", DotNetRef, Element);
                await Module.DisposeAsync();
            }
            DotNetRef?.Dispose();
        }
        catch(Exception ex)
        {
            //
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.Plugins.Base/Components/FormRichTextEditor/FormRichTextEditor.razor.js");

        // TODO: type should be property
        await Module.InvokeVoidAsync("initialize", DotNetRef, Element, new { Value, Readonly, Placeholder, Type = "advanced" });
    }
    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }
}
