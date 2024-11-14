using FluentCMS.Providers.EmailProviders;

namespace FluentCMS.Web.Plugins.ContactUs;

public partial class ContactUsViewPlugin
{
    [Inject]
    private IEmailProvider EmailProvider { get; set; } = default!;

    public const string CONTENT_TYPE_NAME = nameof(ContactUsContent);

    private ContactUsContent Model { get; set; } = new();

    protected async Task OnSubmit()
    {
        Plugin.Settings.TryGetValue("EmailAddress", out var emailAddress);
        emailAddress ??= string.Empty;

        await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());

        if (!string.IsNullOrWhiteSpace(emailAddress))
            await EmailProvider.Send(Model.Email, emailAddress, Model.Subject, Model.Message);
    }
}
