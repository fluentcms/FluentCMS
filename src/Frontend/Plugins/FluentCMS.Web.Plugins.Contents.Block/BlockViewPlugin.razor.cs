namespace FluentCMS.Web.Plugins.Contents.Block;
public partial class BlockViewPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(BlockContent);

    private BlockContent? Item { get; set; }

    private bool AuthenticatedDefault { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        var authenticated = ViewState.Type == ViewStateType.Default && !ViewState.Page.Locked && ViewState.User.Roles.Any(role => role.Type == RoleTypesViewState.Authenticated);
        AuthenticatedDefault = authenticated && ViewState.Type == ViewStateType.Default;
        
        if (Plugin is not null)
        {
            var response = await ApiClient.PluginContent.GetAllAsync(nameof(BlockContent), Plugin.Id);

            if (response?.Data != null && response.Data.ToContentList<BlockContent>().Any())
            {
                Item = response.Data.ToContentList<BlockContent>().FirstOrDefault();
            }
        }
    }
}
