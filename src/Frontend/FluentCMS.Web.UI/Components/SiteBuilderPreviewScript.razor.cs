using AutoMapper;
using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilderPreviewScript : IDisposable
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

    [JSInvokable]
    public async Task CreatePlugin(Guid definitionId, string section, int order)
    {
        var createPluginRequest = new PluginCreateRequest()
        {
            PageId = ViewState.Page.Id,
            Settings = [], // Initialize as an empty dictionary
            DefinitionId = definitionId,
            Section = section,
            Order = order
        };

        await ApiClients.Plugin.CreateAsync(createPluginRequest);

        var pluginsResponse = await ApiClients.Plugin.GetByPageIdAsync(ViewState.Page.Id);
        var plugins = pluginsResponse.Data!.Where(x => x.Section == section).OrderBy(p => p.Order).ToList();

        var pluginOrder = 0;
        foreach (var plugin in plugins)
        {
            pluginOrder += 2;
            plugin.Order = pluginOrder;
            var pluginUpdateRequest = Mapper.Map<PluginUpdateRequest>(plugin);
            await ApiClients.Plugin.UpdateAsync(pluginUpdateRequest);
        }

        ViewState.Reload();
    }

    [JSInvokable]
    public async Task UpdatePlugin(PluginUpdateRequest request)
    {
        await ApiClients.Plugin.UpdateAsync(request);
        ViewState.Reload();
    }

    [JSInvokable]
    public async Task UpdatePluginsOrder(List<PagePluginDetail> plugins)
    {
        var request = new PageUpdatePluginOrdersRequest()
        {
            Plugins = plugins
        };
        await ApiClients.Page.UpdatePluginOrdersAsync(request);
    }

    void ViewStateChanged()
    {
        if (Module is null) return;

        Module.InvokeVoidAsync("update", DotNetObjectReference.Create(this));
    }

    protected override async Task OnInitializedAsync()
    {
        ViewState.ReloadAction += ViewStateChanged;

        await Task.CompletedTask;
    }
    void IDisposable.Dispose()
    {
        ViewState.ReloadAction -= ViewStateChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilderPreviewScript.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), new { });
    }

}