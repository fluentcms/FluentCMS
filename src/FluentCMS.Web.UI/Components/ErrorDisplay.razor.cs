using FluentCMS.Web.UI.Plugins;

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

    private void OnErrorChanged()
    {
        StateHasChanged();
    }

    public void Dispose()
    {
        ErrorContext.ErrorChanged -= OnErrorChanged;
    }
}
