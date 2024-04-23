namespace FluentCMS.Web.UI.Plugins.Settings;

public partial class SettingsViewPlugin
{
    [Inject]
    GlobalSettingsClient GlobalSettingsClient { get; set; } = default!;

    string? SuperUser { get; set; }
    string? Message { get; set; }

    GlobalSettings View = new();
    GlobalSettingsUpdateRequest Model = new();

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var result = await GlobalSettingsClient!.GetAsync();

        if (result?.Data != null)
        {
            View = result.Data;
            Model = new()
            {
                SuperUsers = View.SuperUsers
            };
            SuperUser = View.SuperUsers?.ToList<string>().First();
        }
    }

    async Task OnSubmit()
    {
        try
        {
            await GlobalSettingsClient!.UpdateAsync(new() { SuperUsers = [SuperUser!] });
            Message = "Done!";
        }
        catch (Exception ex)
        {
            Message = ex.ToString();
        }
    }
}
