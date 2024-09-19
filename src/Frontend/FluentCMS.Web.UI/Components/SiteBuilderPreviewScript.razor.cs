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

    public ElementReference element;

    private IJSObjectReference module = default!;

    async Task Reload() {
        ViewState.Reload();
        await Task.CompletedTask;
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

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilderPreviewScript.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), new {});
    }

}