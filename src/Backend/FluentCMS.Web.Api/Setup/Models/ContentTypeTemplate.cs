namespace FluentCMS.Web.Api.Setup.Models;

public class ContentTypeTemplate : ContentType
{
    public List<Dictionary<string, object?>> Contents { get; set; } = [];
}
