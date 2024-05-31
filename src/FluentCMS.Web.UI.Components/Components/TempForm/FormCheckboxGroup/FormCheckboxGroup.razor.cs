using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class FormCheckboxGroup<TItem, TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public ICollection<TItem> Data { get; set; } = [];

    [Parameter]
    public string? TextField { get; set; }

    [Parameter]
    public string? ValueField { get; set; }

    [Parameter]
    public bool Vertical { get; set; }

    [Parameter]
    public EventCallback<ICollection<TValue>> OnChange { get; set; }

    private bool IsChecked(TItem item)
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

    protected override bool TryParseValueFromString(string? value, out ICollection<TValue> result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        throw new NotSupportedException();
    }

    public Task HandleChange(ChangeEventArgs args, TValue value)
    {
        if (Value.Contains(value))
            Value.Remove(value);
        else
            Value.Add(value);

        OnChange.InvokeAsync(Value);
        return Task.CompletedTask;
    }
}
