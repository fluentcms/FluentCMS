namespace FluentCMS.Web.Plugins.Contents.Block;

public partial class BlockEmptyView
{
    [Parameter]
    public EventCallback<BlockDetailResponse> OnBlockSelected { get; set; }

    private List<BlockDetailResponse> Blocks { get; set; } = [];
    private List<BlockDetailResponse> FilteredBlocks { get; set; } = [];
    private List<string> Categories { get; set; } = [];
    private string CurrentCategory { get; set; } = string.Empty;
    private bool BlockModalOpen { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        var response = await ApiClient.Block.GetAllForSiteAsync(ViewState.Site.Id);
        if (response?.Data != null)
        {
            Blocks = [.. response.Data];
            Categories = Blocks.Select(block => block.Category ?? string.Empty).Distinct().ToList();
            FilteredBlocks = Blocks;
        }
    }

    private async Task OpenBlockModal()
    {
        BlockModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task CloseBlockModal()
    {
        BlockModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task ChooseCategory(string category)
    {
        CurrentCategory = category;
        if (string.IsNullOrEmpty(category))
        {
            FilteredBlocks = Blocks;
        }
        else
        {
            FilteredBlocks = Blocks.Where(x => x.Category == category).ToList();
        }
        await Task.CompletedTask;
    }

    private async Task ChooseBlockType(BlockDetailResponse selectedBlock)
    {
        BlockModalOpen = false;
        await OnBlockSelected.InvokeAsync(selectedBlock);
    }
}
