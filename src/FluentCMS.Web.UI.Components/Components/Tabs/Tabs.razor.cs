namespace FluentCMS.Web.UI.Components;

public partial class Tabs
{
    [Parameter]
    public string Value { get; set; }

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    public async void Activate(string name)
    {
        await ValueChanged.InvokeAsync(Value = name);
    }
}
