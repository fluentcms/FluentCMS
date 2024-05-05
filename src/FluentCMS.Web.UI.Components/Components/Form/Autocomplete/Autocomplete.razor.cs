namespace FluentCMS.Web.UI.Components;

public partial class Autocomplete
{
    public ElementReference element;

    private IJSObjectReference module = default!;

    private bool disabled;

    [Parameter]
    public bool Disabled
    {
        get => disabled;
        set
        {
            this.disabled = value;

            module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Disabled });
        }
    }

    [Parameter]
    public bool Multiple { get; set; }

    private List<AutocompleteOptions> options = [];

    [Parameter]
    public List<AutocompleteOptions> Options
    {
        get => options;
        set
        {
            if (this.options.SequenceEqual(value)) return;

            this.options = value;

            module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Options });
        }
    }

    private List<string> value = [];

    [Parameter]
    public List<string> Value
    {
        get => this.value;
        set
        {
            if (this.value.SequenceEqual(value)) return;

            this.value = value;

            module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Value });

            ValueChanged.InvokeAsync(value);
        }
    }

    [Parameter]
    public EventCallback<List<string>> ValueChanged { get; set; }

    [JSInvokable]
    public void UpdateValue(List<string> Value)
    {
        this.Value = Value;
    }

    public async ValueTask DisposeAsync()
    {
        await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/Autocomplete/Autocomplete.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element);
    }
}
