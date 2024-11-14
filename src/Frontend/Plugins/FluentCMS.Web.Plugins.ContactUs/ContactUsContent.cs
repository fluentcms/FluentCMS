namespace FluentCMS.Web.Plugins.ContactUs;

public class ContactUsContent : IContent
{
    public Guid Id { get; set; } = Guid.Empty;
    public string Email { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
};
