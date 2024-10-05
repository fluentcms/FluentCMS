namespace FluentCMS.Web.Plugins.Contents.Block;

public partial class BlockPreviewItem
{
    [Parameter]
    public BlockDetailResponse Block { get; set; } = default!;
}