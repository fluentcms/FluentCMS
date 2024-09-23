namespace FluentCMS.Web.UI.Components;

public partial class FormFileUpload : IAsyncDisposable
{
    #region Copy of BaseInput component Fields
    [Parameter]
    [CSSProperty]
    public bool Disabled { get; set; }

    [Parameter]
    public bool Dense { get; set; }

    [Parameter]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter]
    public string? Hint { get; set; }

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public RenderFragment LabelFragment { get; set; } = default!;

    [Parameter]
    public string? Name { get; set; }

    [Parameter]
    public bool Readonly { get; set; }

    [Parameter]
    public bool Required { get; set; }

    #endregion

    [Inject]
    public IJSRuntime? JS { get; set; } = default!;

    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public string? Accept { get; set; }

    [Parameter]
    public bool Multiple { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnChange { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    public ElementReference Element;

    public IJSObjectReference Module = default!;
    private DotNetObjectReference<FormFileUpload> DotNetRef { get; set; } = default!;

    async Task HandleChange(InputFileChangeEventArgs evt)
    {
        await OnChange.InvokeAsync(evt);
    }

    public async ValueTask DisposeAsync()
    {
        if (Module is not null)
        {
            await Module.InvokeVoidAsync("dispose", DotNetRef, Element);
            await Module.DisposeAsync();
        }
        DotNetRef.Dispose();
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/FormFileUpload/FormFileUpload.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, Element);
    }
}
