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

    private ValidationMessageStore ValidationMessageStore { get; set; } = default!;

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
        {
            ValidationMessageStore = new ValidationMessageStore(EditForm.EditContext!);
        }
    }

    private async Task HandleSubmit(EditContext editContext)
    {
        try
        {
            await OnSubmit.InvokeAsync(editContext);
        }
        catch (ApiClientException ex)
        {
            ValidationMessageStore.Clear();
            if (ex.Data is { Errors: not null and var errors } && errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    ValidationMessageStore.Add(() => Model!,
                        string.IsNullOrEmpty(error.Description!) ? error.Code! : error.Description);
                }

                editContext.NotifyValidationStateChanged();
            }
            else
            {
                ValidationMessageStore.Add(() => Model!, ex.Message);
                editContext.NotifyValidationStateChanged();
            }
        }
    }
}
