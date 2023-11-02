namespace FluentCMS.Application.Dtos.Sites;
public class AddSiteUrlRequest
{
    public Guid SiteId { get; set; }
    public string NewUrl { get; set; } = "";
}
