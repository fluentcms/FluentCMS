namespace FluentCMS.Entities.ContentTypes;

public class ContentTypeField
{
    public ContentTypeField(string title, FieldType fieldType, IDictionary<string, string> options)
    {
        Title = title;
        FieldType = fieldType;
        Options = options;
    }

    public string Title { get; }
    public FieldType FieldType { get; }
    public IDictionary<string, string> Options { get; }
}