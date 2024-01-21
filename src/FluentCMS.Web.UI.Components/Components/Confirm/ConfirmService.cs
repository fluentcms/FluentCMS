namespace FluentCMS.Web.UI.Components;

public class ConfirmService
{
    public ConfirmProvider ConfirmProvider { get; set; } = default!;

    public async Task<bool> Show(string Message)
    {
        return await ConfirmProvider.Show(Message);
    }
}
