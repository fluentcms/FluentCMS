namespace FluentCMS.Services.Setup.Models;

internal class ContentTypeTemplate : ContentType
{
    public List<Dictionary<string, object?>> Contents { get; set; } = [];
}
