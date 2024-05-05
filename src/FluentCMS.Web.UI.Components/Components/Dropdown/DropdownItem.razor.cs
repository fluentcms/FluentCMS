namespace FluentCMS.Web.UI.Components;

public partial class DropdownItem
{
    [Parameter]
    public string? Href { get; set; }

    [CascadingParameter]
    public Dropdown? Parent { get; set; }

    [Parameter]
    public EventCallback OnClick { get; set; }

    private async Task OnClicked()
    {
        Parent?.Close();

        await OnClick.InvokeAsync();
    }
}
