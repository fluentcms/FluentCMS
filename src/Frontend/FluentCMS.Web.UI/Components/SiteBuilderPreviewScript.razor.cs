using AutoMapper;
using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilderPreviewScript : IAsyncDisposable
{
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public IMapper Mapper { get; set; } = default!;

    [Inject]
    public ApiClientFactory ApiClients { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    private IJSObjectReference Module { get; set; } = default!;

    private DotNetObjectReference<SiteBuilderPreviewScript>? DotNetRef { get; set; }

    [JSInvokable]
    public async Task<Guid?> CreatePlugin(Guid definitionId, string section)
    {
        var createPluginRequest = new PluginCreateRequest()
        {
            SiteId = ViewState.Site.Id,
            PageId = ViewState.Page.Id,
            DefinitionId = definitionId,
            Section = section,
        };

        var createPluginResponse = await ApiClients.Plugin.CreateAsync(createPluginRequest);
        if (createPluginRequest != null)
        {
            var plugin = Mapper.Map<PluginViewState>(createPluginResponse.Data);
            plugin.Definition = ViewState.PluginDefinitions.Where(x => x.Id == definitionId).FirstOrDefault() ?? throw new Exception("Plugin definition not found!");
            ViewState.PluginCreated(plugin);
        }

        return createPluginResponse?.Data.Id;
    }

    [JSInvokable]
    public async Task UpdatePlugin(PluginUpdateRequest request)
    {
        await ApiClients.Plugin.UpdateAsync(request);
        ViewState.Reload();
    }

    [JSInvokable]
    public async Task UpdatePluginCols(PluginUpdateColsRequest request)
    {
        await ApiClients.Plugin.UpdateColsAsync(request);
        ViewState.Reload();
    }

    [JSInvokable]
    public async Task UpdatePluginsOrder(List<PluginOrder> plugins)
    {
        var request = new PluginUpdateOrdersRequest()
        {
            Plugins = plugins
        };
        await ApiClients.Plugin.UpdateOrdersAsync(request);

        var pluginsResponse = await ApiClients.Plugin.GetByPageIdAsync(ViewState.Page.Id);
        List<PluginViewState> result = [];
        foreach(var plugin in pluginsResponse.Data ?? [])
        {
            var definition = ViewState.PluginDefinitions.Where(definition => definition.Id == plugin.DefinitionId).FirstOrDefault() ?? throw new Exception("Plugin Definition not found!");
            var mappedPlugin = Mapper.Map<PluginViewState>(plugin);
            mappedPlugin.Definition = definition;
            result.Add(mappedPlugin);
        }

        ViewState.UpdatePluginsOrder(result);
    }

    void ViewStateChanged(object? sender, EventArgs e)
    {
        if (Module is null) return;

        Module.InvokeVoidAsync("update", DotNetObjectReference.Create(this));
    }

    protected override async Task OnInitializedAsync()
    {
        ViewState.OnStateChanged += ViewStateChanged;

        await Task.CompletedTask;
    }
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        ViewState.OnStateChanged -= ViewStateChanged;

        if(Module is not null)
        {
            await Module.DisposeAsync();
        }

        DotNetRef?.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilderPreviewScript.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef);
    }
}