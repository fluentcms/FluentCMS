namespace FluentCMS.Web.Api.Setup;

internal class App
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentType> ContentTypes { get; set; } = [];
}

internal class ContentType
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentTypeField> Fields { get; set; } = [];
}

internal class ContentTypeField
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Label { get; set; } = default!;
    public string? Placeholder { get; set; }
    public string? Hint { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; }
}

internal class Content
{
    public string TypeSlug { get; set; } = default!;
    public Dictionary<string, object?> Value { get; set; } = [];
}

//public interface IAppTemplateService
//{
//    Task<IEnumerable<string>> GetAll();
//    Task<App> Get(string name);
//}

