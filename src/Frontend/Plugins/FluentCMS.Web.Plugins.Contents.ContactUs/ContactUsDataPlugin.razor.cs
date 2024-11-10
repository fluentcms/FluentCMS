namespace FluentCMS.Web.Plugins.Contents.ContactUs;

public partial class ContactUsDataPlugin
{
    private List<ContactUsContent> Items { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var response = await ApiClient.PluginContent.GetAllAsync(nameof(ContactUsContent), Plugin!.Id);
        Items = response.Data?.ToContentList<ContactUsContent>() ?? [];
        
        await base.OnInitializedAsync();
    }
}
