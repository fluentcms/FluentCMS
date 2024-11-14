namespace FluentCMS.Web.Plugins.Block;

public partial class BlockPreviewItem
{
    [Parameter]
    public BlockDetailResponse Block { get; set; } = default!;
}