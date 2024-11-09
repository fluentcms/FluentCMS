namespace FluentCMS.Web.Plugins.Contents.ContactUs;

public partial class ContactUsSettingsPlugin
{
    public const string PLUGIN_SETTINGS_FORM = nameof(ContactUsSettings);

    private ContactUsSettings? Model { get; set; }

    [SupplyParameterFromQuery(Name = nameof(Id))]
    private Guid? Id { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Plugin.Settings.TryGetValue("EmailAddress", out var emailAddress);
        Model = new ContactUsSettings {
            EmailAddress = emailAddress ?? string.Empty
        };
        await base.OnInitializedAsync();
    }

    private async Task HandleSubmit()
    {
        await ApiClient.Settings.UpdateAsync(new SettingsUpdateRequest {
            Id = Plugin.Id,
            Settings = new Dictionary<string, string> {
                { "EmailAddress", Model.EmailAddress }
            }
        });

        await OnSubmit.InvokeAsync();
    }

    class ContactUsSettings
    {
        public string EmailAddress { get; set; } = string.Empty;
    }
}
