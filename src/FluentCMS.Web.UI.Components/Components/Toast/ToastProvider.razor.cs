using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public partial class ToastProvider
{
    [Inject]
    public ToastService ToastService { get; set; } = default!;

    public List<string> toasts = new List<string>();

    public void Info(string Message)
    {

    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ToastService.ToastProvider = this;
    }
}
