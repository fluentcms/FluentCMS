namespace FluentCMS.Entities.ContentTypes;

public class ContentTypeField
{
    public ContentTypeField(string title, FieldType fieldType, IDictionary<string, string> options)
    {
        Title = title;
        FieldType = fieldType;
        Options = options;
    }
    protected ContentTypeField()
    {

    }

    public string Title { get; private set; } = "";
    public FieldType FieldType { get; private set; } = FieldType.Text;
    public IDictionary<string, string> Options { get; private set; } = new Dictionary<string, string>();
}