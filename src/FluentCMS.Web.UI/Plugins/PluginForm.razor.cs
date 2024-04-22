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
    public string? Error { get; set; }

    [Parameter]
    public virtual object? Model { get; set; }

    [Parameter]
    public EventCallback<EditContext> OnSubmit { get; set; }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (string.IsNullOrEmpty(Name))
            throw new ArgumentNullException("FormName");
    }

    private async Task HandleSubmit(EditContext editContext)
    {
        try
        {
            await OnSubmit.InvokeAsync(editContext);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
