namespace FluentCMS.Web.UI;

public partial class Setup
{
    protected override async Task OnInitializedAsync()
    {
        if (await SetupManager.IsInitialized())
            NavigationManager.NavigateTo("/", true);

        await base.OnInitializedAsync();
    }
}
