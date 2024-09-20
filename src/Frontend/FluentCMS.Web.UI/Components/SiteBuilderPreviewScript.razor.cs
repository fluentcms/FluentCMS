using AutoMapper;
using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilderPreviewScript
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

    async Task Reload() {
        ViewState.Reload();
        await Module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), new {});
    }

    [JSInvokable]
    public async Task CreatePlugin(Guid definitionId, string section, int order)
    {
        var createPluginRequest = new PluginCreateRequest()
        {
            PageId = ViewState.Page.Id,
            Settings = new Dictionary<string, string>(), // Initialize as an empty dictionary
            DefinitionId = definitionId,
            Section = section,
            Order = order
        };

        await ApiClients.Plugin.CreateAsync(createPluginRequest);

        var pluginsResponse = await ApiClients.Plugin.GetByPageIdAsync(ViewState.Page.Id);
        var plugins = pluginsResponse.Data!.OrderBy(p => p.Order).ToList();

        var pluginorder = 0;
        foreach (var plugin in plugins)
        {
            plugin.Order = pluginorder++;
            await ApiClients.Plugin.UpdateAsync(Mapper.Map<PluginUpdateRequest>(plugin));
        }

        await Reload();
    }

    [JSInvokable]
    public async Task UpdatePlugin(PluginUpdateRequest request)
    {
        await ApiClients.Plugin.UpdateAsync(request);
        await Reload();
    }

    [JSInvokable]
    public async Task UpdatePluginsOrder(List<PagePluginDetail> plugins)
    {
        var request = new PageUpdatePluginOrdersRequest() 
        {
            Plugins = plugins
        };
        await ApiClients.Page.UpdatePluginOrdersAsync(request);

        await Reload();
    }

    protected override async Task OnInitializedAsync()
    {
        // 
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) 
    {
        if (!firstRender)
            return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilderPreviewScript.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), new {});
    }

}