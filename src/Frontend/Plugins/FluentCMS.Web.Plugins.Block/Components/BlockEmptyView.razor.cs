namespace FluentCMS.Web.Plugins.Block;

public partial class BlockEmptyView
{
    [Parameter]
    public EventCallback<BlockDetailResponse> OnBlockSelected { get; set; }

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

    private async Task ChooseBlockType(BlockDetailResponse selectedBlock)
    {
        BlockModalOpen = false;
        await OnBlockSelected.InvokeAsync(selectedBlock);
    }
}
