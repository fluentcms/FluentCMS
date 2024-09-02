namespace FluentCMS.Web.Plugins.Admin.BlockManagement;

public partial class BlockListPlugin
{
    private List<BlockDetailResponse> Blocks { get; set; } = [];

    public async Task Load()
    {
        var blocksResponse = await ApiClient.Block.GetAllAsync();
        Blocks = blocksResponse?.Data.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    #region Delete Block

    private BlockDetailResponse? SelectedBlock { get; set; }
    public async Task OnDelete()
    {
        if (SelectedBlock == null)
            return;

        await ApiClient.Block.DeleteAsync(SelectedBlock.Id);
        await Load();
        SelectedBlock = default;
    }

    public async Task OnConfirm(BlockDetailResponse block)
    {
        SelectedBlock = block;
        await Task.CompletedTask;
    }
    public async Task OnConfirmClose()
    {
        SelectedBlock = default;
        await Task.CompletedTask;
    }
    #endregion
    
}
