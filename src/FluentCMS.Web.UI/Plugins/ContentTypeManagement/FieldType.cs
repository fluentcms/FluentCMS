namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public class FieldType
{
    public string Title { get; set; } = default!;
    public IconName Icon { get; set; }
    public string Description { get; set; } = default!;
    public string Key { get; set; } = default!;
    public int Order { get; set; }
}

public class FieldTypes : Dictionary<string, FieldType>
{
    public FieldTypes()
    {
        Add("string", new FieldType
        {
            Icon = IconName.Text,
            Title = "Text",
            Description = "Small or long text like title or description",
            Key = "text",
            Order = 1
        });
        Add("number", new FieldType
        {
            Title = "Number",
            Icon = IconName.Number,
            Description = "Numbers (integer, float, decimal)",
            Key = "number",
            Order = 2
        });
        Add("boolean", new FieldType
        {
            Title = "Boolean",
            Icon = IconName.Boolean,
            Description = "Yes or no, 1 or 0, true or false",
            Key = "boolean",
            Order = 3
        });
    }
}
