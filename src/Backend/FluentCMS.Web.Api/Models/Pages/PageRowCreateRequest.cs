namespace FluentCMS.Web.Api.Models;

public class PageRowCreateRequest
{
    public Guid SectionId { get; set; }
    public int Order { get; set; }
}
