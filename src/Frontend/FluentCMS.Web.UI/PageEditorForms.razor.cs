namespace FluentCMS.Web.UI;

public partial class PageEditorForms
{
    #region Base Plugin

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    // due to open issue in NavigationManager
    // https://github.com/dotnet/aspnetcore/issues/55685
    // https://github.com/dotnet/aspnetcore/issues/53996
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

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    [SupplyParameterFromForm(FormName = "UpdatePluginForm")]
    private PageEditorSaveRequest Model { get; set; } = new();

    [SupplyParameterFromForm(FormName = "AddSectionForm")]
    private AddSectionRequest AddSectionModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "AddColumnForm")]
    private AddColumnRequest AddColumnModel { get; set; } = new();
    
    [SupplyParameterFromForm(FormName = "ColumnUpdateForm")]
    private ColumnsUpdateRequest ColumnUpdateModel { get; set; } = new();
    
    private async Task OnColumnUpdateSubmit()
    {
        foreach (var column in ColumnUpdateModel.Columns)
        {
            Console.WriteLine($"column.Id: {column.Id}");
            Console.WriteLine($"column.Order: {column.Order}");
            Console.WriteLine($"column.RowId: {column.RowId}");
            Console.WriteLine($"column.Styles: {column.Styles}");
            // Console.WriteLine($"co   lumn.Styles['cols']: {column.Styles["Cols"]}");

            await ApiClient.Page.UpdateColumnAsync(column);
            Console.WriteLine("________");
            foreach (var key in column.Styles.Keys)
            {

                Console.WriteLine($"key: {key}, value: {column.Styles[key]}");
            }
            Console.WriteLine("________");

            // 
        }
    }

    private async Task OnAddColumnSubmit()
    {
        if (AddColumnModel.Mode == 1)
        {
            var column = new PageColumnCreateRequest 
            {
                RowId = AddColumnModel.RowId,
                Order = 0,
                Styles = new Dictionary<string, string> 
                {
                    {"Cols", "12"},
                    {"ColsSm", "0"},
                    {"ColsMd", "0"},
                    {"ColsLg", "0"},
                }
            };

            var response = await ApiClient.Page.CreateColumnAsync(column);
        }
        else if(AddColumnModel.Mode == 2)
        {
            var column = new PageColumnCreateRequest 
            {
                RowId = AddColumnModel.RowId,
                Order = 0,
                Styles = new Dictionary<string, string> 
                {
                    {"Cols", "6"},
                    {"ColsSm", "0"},
                    {"ColsMd", "0"},
                    {"ColsLg", "0"},
                }
            };

            await ApiClient.Page.CreateColumnAsync(column);
            await ApiClient.Page.CreateColumnAsync(column);
        }
        else if(AddColumnModel.Mode == 3)
        {
            var column = new PageColumnCreateRequest 
            {
                RowId = AddColumnModel.RowId,
                Order = 0,
                Styles = new Dictionary<string, string> 
                {
                    {"Cols", "4"},
                    {"ColsSm", "0"},
                    {"ColsMd", "0"},
                    {"ColsLg", "0"},
                }
            };

            await ApiClient.Page.CreateColumnAsync(column);
            await ApiClient.Page.CreateColumnAsync(column);
            await ApiClient.Page.CreateColumnAsync(column);
        }
        else if(AddColumnModel.Mode == 4)
        {
            var column = new PageColumnCreateRequest 
            {
                RowId = AddColumnModel.RowId,
                Order = AddColumnModel.Order,
                Styles = new Dictionary<string, string> 
                {
                    {"Cols", "2"},
                    {"ColsSm", "0"},
                    {"ColsMd", "0"},
                    {"ColsLg", "0"},
                }
            };

            await ApiClient.Page.CreateColumnAsync(column);
        }
    }

    private async Task OnAddSectionSubmit()
    {
        var section = new PageSectionCreateRequest 
        {
            PageId = ViewState.Page.Id,
            Order = AddSectionModel.Order,
            Styles = []
        };

        var response = await ApiClient.Page.CreateSectionAsync(section);

        if (response?.Data?.Id != null)
        {
            var row = new PageRowCreateRequest
            {
                SectionId = response.Data.Id,
                Order = 0
            };

            var response2 = await ApiClient.Page.CreateRowAsync(row);
        }

    }
    private async Task OnUpdateSubmit()
    {
        foreach (var deletedId in Model.DeleteIds ?? [])
        {
            await ApiClient.Plugin.DeleteAsync(deletedId);
        }

        foreach (var plugin in Model.CreatePlugins ?? [])
        {
            plugin.PageId = ViewState.Page.Id;

            await ApiClient.Plugin.CreateAsync(plugin);
        }

        foreach (var plugin in Model.UpdatePlugins ?? [])
        {
            await ApiClient.Plugin.UpdateAsync(plugin);
        }

        NavigateBack();
    }

    class AddSectionRequest
    {
        public bool Submitted { get; set; } = true;
        public int Order { get; set; } = 0;
    };

    class AddColumnRequest
    {
        public bool Submitted { get; set; } = true;
        public int Mode { get; set; } = 1;
        public Guid RowId { get; set; }
        public int Order { get; set; } = 0;
    };

    class ColumnsUpdateRequest
    {
        public bool Submitted { get; set; } = true;
        public List<PageColumnUpdateRequest> Columns { get; set; } = [];
    };

    class PageEditorSaveRequest
    {
        public bool Submitted { get; set; } = true;
        public List<Guid> DeleteIds { get; set; } = [];
        public List<PluginCreateRequest> CreatePlugins { get; set; } = [];
        public List<PluginUpdateRequest> UpdatePlugins { get; set; } = [];
    }
}
