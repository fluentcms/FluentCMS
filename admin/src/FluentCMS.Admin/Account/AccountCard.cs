using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Account;

// A card of the account page, inert while the page's hold is set: its controls are disabled and its actions do nothing.
public abstract class AccountCard : ComponentBase, IDisposable
{
    [CascadingParameter]
    internal AccountHold? Hold { get; set; }

    protected bool Held => Hold?.Held == true;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized()
    {
        if (Hold is not null)
        {
            Hold.Changed += OnHoldChanged;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Hold is not null)
        {
            Hold.Changed -= OnHoldChanged;
        }
    }

    private void OnHoldChanged() => _ = InvokeAsync(StateHasChanged);
}
