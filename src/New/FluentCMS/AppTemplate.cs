namespace FluentCMS.AppTemplates;

public class AppTemplate
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentType> ContentTypes { get; set; } = [];

}


//public class System
//{
//    public User SuperAdmin { get; set; } = default!;
//}

//public class User
//{
//    public string Username { get; set; } = default!;
//    public string Password { get; set; } = default!;
//}

public class ContentType
{
    public string Slug { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public List<ContentTypeField> Fields { get; set; } = [];
}

public class ContentTypeField
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

public class Content
{
    public string TypeSlug { get; set; } = default!;
    public Dictionary<string, object?> Value { get; set; } = [];
}

public interface IAppTemplateService
{
    Task<IEnumerable<string>> GetAll();
    Task<AppTemplate> Get(string name);
}

