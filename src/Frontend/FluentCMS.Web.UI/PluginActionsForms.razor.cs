namespace FluentCMS.Web.UI;

public partial class PluginActionsForms
{
    #region Base Plugin

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path);
    }

    protected virtual void NavigateBack()
    {
        var url = new Uri(NavigationManager.Uri).LocalPath;
        NavigateTo(url);
    }

    #endregion

    [SupplyParameterFromForm(FormName = "DeletePluginItemForm")]
    private DeletePluginItemRequest DeletePluginItemModel { get; set; } = new();

    private async Task OnDeletePluginItemSubmit()
    {
        await ApiClient.PluginContent.DeleteAsync(DeletePluginItemModel.PluginTypeName, DeletePluginItemModel.PluginId, DeletePluginItemModel.ItemId);

        NavigateBack();
    }

    class DeletePluginItemRequest
    {
        public Guid ItemId { get; set; }
        public Guid PluginId { get; set; }
        public string PluginTypeName { get; set; }
    }
}
