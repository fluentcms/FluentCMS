namespace FluentCMS.Web.UI;
public partial class PageEditorSidebar
{
    [Parameter]
    public List<BlockDetailResponse> Blocks { get; set; } = [];

    [Parameter]
    public List<PluginDefinitionDetailResponse> PluginDefinitions { get; set; } = [];

    private Guid BlockDefinitionId { get; set; } = Guid.Empty;

    protected override async Task OnInitializedAsync()
    {
        var blockDefinition = PluginDefinitions.Where(x => x.Name == "Block Plugin").FirstOrDefault();

        if (blockDefinition?.Id != null)
        {
            BlockDefinitionId = blockDefinition.Id;
        }
    }
}
