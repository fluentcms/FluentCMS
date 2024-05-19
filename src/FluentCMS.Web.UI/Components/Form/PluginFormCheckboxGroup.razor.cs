namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormCheckboxGroup<TItem, TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public ICollection<TItem> Data { get; set; } = [];

    [Parameter]
    public Func<TItem, string>? TextField { get; set; } = default!;

    [Parameter]
    public string? ValueField { get; set; }

    [Parameter]
    public bool Vertical { get; set; }

    private bool IsChecked(TItem item)
    {
        return Value?.Contains(GetValue(item)) ?? false;
    }

    private TValue? GetValue(TItem item)
    {
        if (string.IsNullOrEmpty(ValueField))
        {
            return default!;
        }
        return (TValue?)item!.GetType().GetProperty(ValueField)?.GetValue(item);
    }

    private void OnValueChange(TItem item)
    {
        var value = Value?.ToList() ?? new ();

        if (IsChecked(item))
        {
            value.Remove(GetValue(item));
        }
        else
        {
            value.Add(GetValue(item));
        }

        ValueChanged.InvokeAsync(value);
    }
}
