using FluentCMS.Web.UI.Plugins;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace FluentCMS.Web.UI.Components;
public partial class ErrorDisplay
{
    [CascadingParameter]
    public ErrorContext ErrorContext { get; set; } = default!;

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
        {
            ErrorContext.ErrorChanged += OnErrorChanged;
        }
    }

    private void OnErrorChanged(object? sender, EventArgs e)
    {
        StateHasChanged();
    }

    public void Dispose()
    {
        ErrorContext.ErrorChanged -= OnErrorChanged;
    }
}
