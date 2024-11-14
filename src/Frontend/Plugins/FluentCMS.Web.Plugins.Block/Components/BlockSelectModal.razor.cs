namespace FluentCMS.Web.Plugins.Block;

public partial class BlockSelectModal
{
    [Parameter]
    public EventCallback<BlockDetailResponse> OnBlockSelected { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter]
    public bool Open { get; set; } = false;

    private List<BlockDetailResponse> Blocks { get; set; } = [];
    private List<BlockDetailResponse> FilteredBlocks { get; set; } = [];
    private List<string> Categories { get; set; } = [];
    private string CurrentCategory { get; set; } = string.Empty;

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
        await OnBlockSelected.InvokeAsync(selectedBlock);
    }

    private async Task HandleClose()
    {
        await OnClose.InvokeAsync();
    }
}
