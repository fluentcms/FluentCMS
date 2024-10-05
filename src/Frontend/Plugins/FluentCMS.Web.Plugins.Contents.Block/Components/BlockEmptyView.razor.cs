namespace FluentCMS.Web.Plugins.Contents.Block;

public partial class BlockEmptyView
{
    [Parameter]
    public Guid PluginId { get; set; } = Guid.Empty;

    private List<BlockDetailResponse> Blocks { get; set; } = [];
    private List<BlockDetailResponse> FilteredBlocks { get; set; } = [];
    private List<string?> Categories { get; set; } = [];
    private string CurrentCategory { get; set; } = string.Empty;
    private bool BlockModalOpen { get; set; } = false;

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

    private async Task ChooseCategory(string? category)
    {
        CurrentCategory = category ?? string.Empty;
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

    private void ReloadPage()
    {
        ViewState.Reload();
    }

    private async Task ChooseBlockType(BlockDetailResponse block)
    {
        await ApiClient.PluginContent.CreateAsync(nameof(BlockContent), PluginId, new Dictionary<string, object>
        {
            { "Content", block.Content! }
        });

        BlockModalOpen = false;
        ReloadPage();
    }

    private async Task Load()
    {
        var response = await ApiClient.Block.GetAllForSiteAsync(ViewState.Site.Id);
        if (response?.Data != null)
        {
            Blocks = response.Data.ToList();
            Categories = Blocks.Select(block => block.Category)
                .Distinct()
                .ToList();

            FilteredBlocks = Blocks;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }
}