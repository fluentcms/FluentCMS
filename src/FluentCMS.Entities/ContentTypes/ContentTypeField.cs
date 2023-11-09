namespace FluentCMS.Entities;

public class ContentTypeField
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    public FieldType FieldType { get; private set; } = FieldType.Text;
    public IDictionary<string, string> Options { get; private set; } = new Dictionary<string, string>();
}