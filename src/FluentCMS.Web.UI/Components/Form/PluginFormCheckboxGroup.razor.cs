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
}
