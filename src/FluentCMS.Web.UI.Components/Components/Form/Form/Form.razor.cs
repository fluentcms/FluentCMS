namespace FluentCMS.Web.UI.Components;

public partial class Form
{
    [Parameter]
    public string? FormName { get; set; }

    [Parameter]
    public string? Method { get; set; }

    [Parameter]
    public object? Model { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    EditContext? Context;

    protected override void OnInitialized()
    {
        if (Model != null)
        {
            Context = new EditContext(Model);
        }
    }

    async Task OnValidSubmit()
    {
        await OnSubmit.InvokeAsync();
    }
}