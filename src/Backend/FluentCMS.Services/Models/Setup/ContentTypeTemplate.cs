namespace FluentCMS.Services.Models.Setup;

public class ContentTypeTemplate : ContentType
{
    public List<Dictionary<string, object?>> Contents { get; set; } = [];
}
