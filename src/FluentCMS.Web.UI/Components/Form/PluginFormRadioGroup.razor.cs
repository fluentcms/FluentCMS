namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormRadioGroup<TItem, TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public TItem[] Data { get; set; } = [];

    [Parameter]
    public Func<TItem, string>? TextField { get; set; } = default!;

    [Parameter]
    public string? ValueField { get; set; }

    [Parameter]
    public bool Vertical { get; set; }

    private TValue? GetValue(TItem item)
    {
        if (string.IsNullOrEmpty(ValueField))
        {
            return default!;
        }
        return (TValue?)item!.GetType().GetProperty(ValueField)?.GetValue(item);
    }

    private RenderFragment RenderBase() => (builder) => base.BuildRenderTree(builder);
}
