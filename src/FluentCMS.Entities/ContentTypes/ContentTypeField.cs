namespace FluentCMS.Entities.ContentTypes;

public class ContentTypeField
{
    public ContentTypeField(string title,
                            FieldType fieldType,
                            IDictionary<string, string>? options = null,
                            string label = "",
                            string description = "",
                            bool hidden = false,
                            string defaultValue = "")
    {
        Title = title;
        Description = description;
        Label = label;
        Hidden = hidden;
        DefaultValue = defaultValue;
        FieldType = fieldType;
        Options = options ?? new Dictionary<string, string>();
    }
    protected ContentTypeField()
    {

    }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Hidden { get; set; }
    public string DefaultValue { get; set; } = "";
    public FieldType FieldType { get; private set; } = FieldType.Text;
    public IDictionary<string, string> Options { get; private set; } = new Dictionary<string, string>();
}