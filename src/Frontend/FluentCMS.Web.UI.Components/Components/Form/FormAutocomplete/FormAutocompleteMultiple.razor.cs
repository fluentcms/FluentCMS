using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Components;

public partial class FormAutocompleteMultiple<TItem, TValue> : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public ICollection<TItem> Data { get; set; } = [];

    [Parameter]
    public string? TextField { get; set; }

    [Parameter]
    public string? ValueField { get; set; }

    [Parameter]
    public EventCallback<ICollection<TValue>> OnChange { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    public ElementReference Element;

    private IJSObjectReference Module { get; set; } = default!;

    private bool IsSelected(TItem item)
    {
        return Value?.Contains(GetValue(item)) ?? false;
    }

    private string? GetText(TItem item)
    {
        if (string.IsNullOrEmpty(TextField))
        {
            return item!.ToString();
        }
        return (string?)item!.GetType().GetProperty(TextField)?.GetValue(item);
    }

    private TValue? GetValue(TItem item)
    {
        if (string.IsNullOrEmpty(ValueField))
        {
            return (TValue?)(object)item!;
        }
        return (TValue?)item!.GetType().GetProperty(ValueField)?.GetValue(item);
    }

    [JSInvokable]
    public async Task UpdateValue(ICollection<TValue> value)
    {
        Value ??= [];

        Value = value;
        // if (Value.Contains(value))
        //     Value.Remove(value);
        // else
        //     Value.Add(value);

        await ValueChanged.InvokeAsync(Value);
        await OnChange.InvokeAsync(Value);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Module != null)
            await Module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), Element, new { Value });
    }

    public async ValueTask DisposeAsync()
    {
        if (Module is null) return;

        await Module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), Element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/FormAutocomplete/FormAutocomplete.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), Element, new { Multiple = true });
    }

    protected override bool TryParseValueFromString(string? value, out ICollection<TValue> result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        throw new NotSupportedException();
    }
}

