using Microsoft.AspNetCore.Components.Forms;

namespace FluentCMS.Web.UI.Plugins;

public partial class PluginForm
{
    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string Name { get; set; } = default!; // Form Name

    [Parameter]
    public virtual object? Model { get; set; }

    [Parameter]
    public EventCallback<EditContext> OnSubmit { get; set; }

    private EditForm EditForm { get; set; } = default!;

    public List<string> Errors { get; set; } = new();

    private async Task HandleSubmit(EditContext editContext)
    {
        try
        {
            await OnSubmit.InvokeAsync(editContext);
        }
        catch (ApiClientException ex)
        {
            Errors.Clear();
            if (ex.Data is { Errors: not null and var errors } && errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    Errors.Add(string.IsNullOrEmpty(error.Description!) ? error.Code! : error.Description);
                }

                StateHasChanged();
            }
            else
            {
                Errors.Add(ex.Message);
                StateHasChanged();
            }
        }
    }
}
