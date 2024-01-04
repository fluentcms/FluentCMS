using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Core.Confirm;
public partial class ConfirmProvider
{
    [Parameter]
    public string Message { get; set; } = "Are you sure?";
    [Inject]
    public ConfirmService ConfirmService { get; set; } = default!;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ConfirmService.ConfirmProvider = this;
    }
    public bool IsOpen { get; set; } = false;
    public void Show(string message)
    {
        Message = message;
        IsOpen = true;
        StateHasChanged();
    }
}
