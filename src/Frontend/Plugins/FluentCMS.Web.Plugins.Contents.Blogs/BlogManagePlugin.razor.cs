namespace FluentCMS.Web.Plugins.Contents.Blogs;

public partial class BlogManagePlugin
{
    // #region Base

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public string? SectionName { get; set; }

    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path, true);
    }

    protected virtual string GetDetailUrl(string viewName, object? parameters = null)
    {
        var uri = new Uri(NavigationManager.Uri);
        var oldQueryParams = HttpUtility.ParseQueryString(uri.Query);

        // this gets the page path from root without QueryString
        var pagePathWithoutQueryString = uri.GetLeftPart(UriPartial.Path);

        var newQueryParams = new Dictionary<string, string?>()
        {
            { "pluginId", Plugin!.Id.ToString() },
            { "viewMode", "detail" },
            { "viewName", viewName }
        };

        if (parameters != null)
        {
            foreach (var propInfo in parameters.GetType().GetProperties())
                newQueryParams[propInfo.Name] = propInfo.GetValue(parameters)?.ToString();
        }

        foreach (var key in oldQueryParams.AllKeys)
        {
            if (string.IsNullOrEmpty(key) || newQueryParams.ContainsKey(key))
                continue;

            newQueryParams[key] = oldQueryParams[key];
        }

        return QueryHelpers.AddQueryString(pagePathWithoutQueryString, newQueryParams);
    }
    // #endregion

        public const string CONTENT_TYPE_NAME = nameof(BlogContent);

    private BlogManagementState CurrentState { get; set; } = BlogManagementState.List;
    private List<BlogContent> Items { get; set; }
    private bool DeleteConfirmOpen { get; set; } = false;

    private async Task Load()
    {
        Console.WriteLine("Load");
        var response = await ApiClient.PluginContent.GetAllAsync(CONTENT_TYPE_NAME, Plugin.Id);

        if (response?.Data != null && response.Data.ToContentList<BlogContent>().Any())
            Items = response.Data.ToContentList<BlogContent>();
    }

    protected virtual async Task NavigateBack()
    {
        if (CurrentState == BlogManagementState.List)
        {
            var url = new Uri(NavigationManager.Uri).LocalPath;
            NavigateTo(url + "?pageEdit=true");
        }
        else
        {
            await Load();
            CurrentState = BlogManagementState.List;
        }
    }

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private BlogContent? Model { get; set; }

    [SupplyParameterFromQuery(Name = nameof(Id))]
    private Guid? Id { get; set; } = default!;

    protected virtual string GetBackUrl()
    {
        return new Uri(NavigationManager.Uri).LocalPath;
    }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            if (Id != null)
            {
                var response = await ApiClient.PluginContent.GetByIdAsync(CONTENT_TYPE_NAME, Plugin!.Id, Id.Value);

                var content = response.Data.Data.ToContent<BlogContent>();

                Model = new BlogContent
                {
                    Id = Id.Value,
                    Content = content.Content,
                    Title = content.Title,
                    Description = content.Description,
                };
            }
            else
            {
                Model = new BlogContent();
            }
        }

        if (Items is null)
        {
            await Load();
        }
    }

    private async Task OnDeleteItem()
    {
        await ApiClient.PluginContent.DeleteAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.Id);
        await Load();
    }

    private async Task OnConfirmClose()
    {
        DeleteConfirmOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnDeleteItemClicked(BlogContent item) 
    {
        DeleteConfirmOpen = true;
        Model = item;
    }

    private async Task OnEditItemClicked(BlogContent item) 
    {
        CurrentState = BlogManagementState.Edit;
        Model = item;
    }

    private async Task OnAddItemClicked()
    {
        CurrentState = BlogManagementState.Create;
        Model = new BlogContent();
    }

    private async Task OnEditBlog(BlogContent model)
    {
        await ApiClient.PluginContent.UpdateAsync(CONTENT_TYPE_NAME, Plugin.Id, model.Id, Model.ToDictionary());
        await NavigateBack();
    }

    private async Task OnCreateBlog(BlogContent model)
    {
        await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());
        await NavigateBack();
    }
}

public enum BlogManagementState
{
    List,
    Create,
    Edit,
    Delete
}
