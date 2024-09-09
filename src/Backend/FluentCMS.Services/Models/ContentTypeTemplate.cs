namespace FluentCMS.Services.Models;

public class ContentTypeTemplate : ContentType
{
    public List<Dictionary<string, object?>> Contents { get; set; } = [];
}
