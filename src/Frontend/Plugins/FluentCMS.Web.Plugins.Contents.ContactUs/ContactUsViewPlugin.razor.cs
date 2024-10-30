namespace FluentCMS.Web.Plugins.Contents.ContactUs;

public partial class ContactUsViewPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(ContactUsContent);

    private string EmailAddress { get; set; } = default!; 

    private ContactUsContent Model { get; set; } = new();
    
    protected override async Task OnInitializedAsync()
    {
        Plugin.Settings.TryGetValue("EmailAddress", out var emailAddress);
        EmailAddress = emailAddress ?? string.Empty;
        await base.OnInitializedAsync();
    }

    protected async Task OnSubmit()
    {
        Console.WriteLine($"Should send email: {Model.Email} - {Model.Subject} - {Model.Message}");

        await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());

        // TODO Import service
        // await emailProvider.Send(Model.Email, Model.Subject, Model.Message, cancellationToken);
    }
}
