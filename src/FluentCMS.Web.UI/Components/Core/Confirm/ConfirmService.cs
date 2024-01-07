namespace FluentCMS.Web.UI.Components.Core.Confirm;

public class ConfirmService
{
    public ConfirmProvider ConfirmProvider { get; set; } = default!;

    public async Task<bool> Show(string Message)
    {
        return await ConfirmProvider.Show(Message);
    }
}
