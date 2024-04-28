﻿using Microsoft.AspNetCore.Components.Forms;

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

    private string? Error { get; set; }

    private async Task HandleSubmit(EditContext editContext)
    {
        try
        {
            await OnSubmit.InvokeAsync(editContext);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            StateHasChanged();
        }
    }
}
