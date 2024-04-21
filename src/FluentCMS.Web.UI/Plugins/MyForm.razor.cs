using Microsoft.AspNetCore.Components.Forms;

namespace FluentCMS.Web.UI.Plugins;

public partial class MyForm
{
    [Parameter]
    public string Name { get; set; } = default!; // Form Name

    [Parameter]
    public string Title { get; set; } = "Form title"; // Form title display

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string? Error { get; set; }

    [Parameter]
    public string? Message { get; set; }

    [Parameter]
    public virtual object? Model { get; set; }

    private bool _showMessage = false;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (string.IsNullOrEmpty(Name))
            throw new ArgumentNullException("FormName");
    }
    private async Task HandleValidSubmit(EditContext editContext)
    {
        try
        {
            await OnSubmit.InvokeAsync(editContext);
            _showMessage = true;
        }
        catch (NavigationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
    [Parameter]
    public EventCallback<EditContext> OnSubmit { get; set; }
}
