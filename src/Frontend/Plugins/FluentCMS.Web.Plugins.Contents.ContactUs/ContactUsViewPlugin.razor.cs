namespace FluentCMS.Web.Plugins.Contents.ContactUs;

public partial class ContactUsViewPlugin
{
    public const string CONTENT_TYPE_NAME = nameof(ContactUsContent);

    // [Inject]
    // private IEmailProvider emailProvider { get; set; } = default!;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public string? SectionName { get; set; }

    [Parameter]
    public PluginViewState? Plugin { get; set; } = default!;

    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    protected virtual void NavigateBack()
    {
        var url = new Uri(NavigationManager.Uri).LocalPath;
        NavigateTo(url);
    }

    protected virtual void NavigateTo(string path)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path);
    }

    private ContactUsSettings Settings { get; set; }

    [SupplyParameterFromForm(FormName = CONTENT_TYPE_NAME)]
    private ContactUsContent Model { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        Settings = Plugin.Settings.ToPluginSettings<ContactUsSettings>();
    }

    protected async Task OnSubmit()
    {
        Console.WriteLine($"Should send email: {Model.Email} - {Model.Subject} - {Model.Message}");

        await ApiClient.PluginContent.CreateAsync(CONTENT_TYPE_NAME, Plugin.Id, Model.ToDictionary());

        // TODO Import service
        // await emailProvider.Send(Model.Email, Model.Subject, Model.Message, cancellationToken);
    }
}
