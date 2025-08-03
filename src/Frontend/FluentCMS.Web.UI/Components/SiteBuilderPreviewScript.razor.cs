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
        var createPluginRequest = new PluginInitialCreateRequest()
        {
            PageId = ViewState.Page.Id,
            DefinitionId = definitionId,
            Section = section
        };
        var createPluginResponse = await ApiClients.Plugin.InitialCreateAsync(createPluginRequest);
        ViewState.Plugins.Add(Mapper.Map<PluginViewState>(createPluginResponse?.Data));
        return createPluginResponse?.Data.Id;
    }

    [JSInvokable]
    public async Task UpdatePluginCols(PluginUpdateColsRequest request)
    {
        await ApiClients.Plugin.UpdateColsAsync(request);
    }

    [JSInvokable]
    public async Task UpdatePluginsOrder(List<PluginOrder> pluginOrders)
    {
        var pluginsResponse = await ApiClients.Plugin.UpdateOrdersAsync(new PluginUpdateOrdersRequest { PluginOrders = pluginOrders });
        var plugins = pluginsResponse?.Data ?? [];
        ViewState.Plugins.ForEach(p =>
        {
            var plugin = plugins.FirstOrDefault(x => x.Id == p.Id);
            if (plugin is not null)
            {
                p.Order = plugin.Order;
                p.Section = plugin.Section!;
            }
        });
        ViewState.StateChanged();
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
        try
        {
            if (Module is not null)
            {
                await Module.DisposeAsync();
            }

            DotNetRef?.Dispose();
        } 
        catch(Exception ex)
        {
            // 
        }
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
