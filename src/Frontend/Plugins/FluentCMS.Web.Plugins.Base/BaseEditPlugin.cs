namespace FluentCMS.Web.Plugins;

public abstract class BaseEditPlugin : ComponentBase
{
    [Inject]
    protected IMapper Mapper { get; set; } = default!;

    [Inject]
    protected ViewState ViewState { get; set; } = default!;

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Parameter]
    public bool Open { get; set; } = false;

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;
}
