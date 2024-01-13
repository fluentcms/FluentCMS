using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public partial class ConfirmProvider
{
    [Inject]
    public ConfirmService ConfirmService { get; set; } = default!;

    public string? Message { get; set; }

    public bool Open { get; set; }

    public TaskCompletionSource<bool> Wating;

    public async Task<bool> Show(string Message)
    {
        this.Message = Message;

        Open = true;

        StateHasChanged();

        Wating = new TaskCompletionSource<bool>();

        await Wating.Task;

        return Wating.Task.Result;

    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ConfirmService.ConfirmProvider = this;
    }

    public void OnCancel()
    {
        Wating.SetResult(false);
    }

    public void OnConfirm()
    {
        Wating.SetResult(true);
    }
}
