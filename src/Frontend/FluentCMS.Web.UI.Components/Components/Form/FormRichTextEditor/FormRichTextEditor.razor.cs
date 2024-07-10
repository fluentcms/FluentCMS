using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Components;

public partial class FormRichTextEditor
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    public ElementReference element;

    private IJSObjectReference module = default!;

    private List<AssetDetail> Assets { get; set; } = [];
    private List<PageDetailResponse> Pages { get; set; } = [];
    private bool LinkModalOpen { get; set; } = false;
    private bool ImageModalOpen { get; set; } = false;
    private string ImageMode = "Library";
    private string? ImageUrl { get; set; }
    private Guid? ImageId { get; set; }
    private string? ImageAlt { get; set; }
    private string? Href { get; set; }
    private string? Text { get; set; }
    private string Mode { get; set; } = "Page";

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
        public string Mode { get; set; }
        public string Text { get; set; }
        public string Href { get; set; }
    }

    public class OpenImageParams
    {
        public string Mode { get; set; }
        public Guid? Id { get; set; }
        public string? Url { get; set; }
        public string? Alt { get; set; }
    }
    

    [JSInvokable]
    public async Task OpenLinkModal(OpenLinkParams value)
    {
        Text = value.Text ?? "";
        Mode = value.Mode ?? "External";
        Href = value.Href ?? "";
        LinkModalOpen = true;
        StateHasChanged();
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
    }

    private async Task OnLinkClear()
    {
        LinkModalOpen = false;
        
        if (module != null)
            await module.InvokeVoidAsync("setLink", DotNetObjectReference.Create(this), element, new { Mode = "Clear"});

    }

    private async Task OnLinkModalClose()
    {
        LinkModalOpen = false;
    }

    private async Task OnChooseExternal() 
    {

        LinkModalOpen = false;

        if (module != null)
            await module.InvokeVoidAsync("setLink", DotNetObjectReference.Create(this), element, new { Href = Href, Text = Text, Mode = "External" });

    }

    private async Task OnChooseFile(AssetDetail file) 
    {
        Text = file.Name;
        Href = $"/API/File/Download/{file.Id}";

        LinkModalOpen = false;

        if (module != null)
            await module.InvokeVoidAsync("setLink", DotNetObjectReference.Create(this), element, new { Href = Href, Text = Text, Mode = "File" });

    }

    string GetPageUrl(Guid? pageId)
    {
        var page = Pages.Where(x => x.Id == pageId).FirstOrDefault();

        if (page.ParentId != null)
        {
            return GetPageUrl(page.ParentId) + page.Path;
        }
        return page.Path;
    }

    private async Task OnChoosePage(PageDetailResponse page) 
    {
        Text = page.Title;
        Href = GetPageUrl(page.Id);

        LinkModalOpen = false;

        if (module != null)
            await module.InvokeVoidAsync("setLink", DotNetObjectReference.Create(this), element, new { Href = Href, Text = Text, Mode = "Page" });

    }
    
    protected override async Task OnInitializedAsync()
    {
        // TODO: Site url
        var pagesResponse = await ApiClient.Page.GetAllAsync("localhost:5000");
        if(pagesResponse?.Data != null)
        {
            Pages = pagesResponse.Data.ToList();
        }

        await OnNavigateFolder(null);
    }

    protected override async Task OnParametersSetAsync()
    {
        base.OnParametersSetAsync();
        if (Value == _value) return;

        _value = Value;

        if (module != null)
            await module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Value });
    }

    public async ValueTask DisposeAsync()
    {
        if (module != null)
            await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    FolderDetailResponse? FindFolderById(ICollection<FolderDetailResponse> folders, Guid folderId)
    {
        foreach (var folder in folders)
        {
            if (folder.Id == folderId)
                return folder;

            if (folder.Folders != null && folder.Folders.Any())
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
        FolderDetailResponse? folderDetail = default!;

        var folderDetailResponse = await ApiClient.Folder.GetAllAsync();

        if (folderId is null || folderId == Guid.Empty)
        {
            folderDetail = folderDetailResponse?.Data;
        }
        else
        {
            folderDetail = FindFolderById(folderDetailResponse?.Data?.Folders, folderId.Value);
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
                    Id = folderDetail.FolderId == Guid.Empty ? null : folderDetail.FolderId,
                    IsParentFolder = true
                });
            }

            foreach (var item in folderDetail.Folders)
            {
                Assets.Add(new AssetDetail
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
                Assets.Add(new AssetDetail
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

    public async Task OnChooseImage(AssetDetail image)
    {
        ImageModalOpen = false;

        ImageUrl = "/API/File/Download/" + image.Id.Value;
        ImageAlt = "(TODO) Image ALT";

        if (module != null)
            await module.InvokeVoidAsync("setImage", DotNetObjectReference.Create(this), element, new { Alt = ImageAlt, Url = ImageUrl });

    }

    public async Task OnImageModalClose()
    {
        ImageModalOpen = false;   
    }

    public async Task OnChooseImageExternal()
    {
        ImageModalOpen = false;   

        if (module != null)
            await module.InvokeVoidAsync("setImage", DotNetObjectReference.Create(this), element, new { Alt = ImageAlt, Url = ImageUrl });

    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/FormRichTextEditor/FormRichTextEditor.razor.js");

        // TODO: type should be property
        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element, new { Value = Value, Readonly = Readonly, Placeholder = Placeholder, Type = "advanced" });
    }
    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }
}