namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

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
            Title = "String",
            Description = "Small or long text like title or description",
            Key = "string",
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
        Add("enumeration", new FieldType
        {
            Title = "Enumeration",
            Icon = IconName.List,
            Description = "List of values, then pick one",
            Key = "enumeration",
            Order = 4
        });
        Add("date", new FieldType
        {
            Title = "Date",
            Icon = IconName.Clock,
            Description = "A date picker with hours, minutes and seconds",
            Key = "date",
            Order = 5
        });
        Add("media", new FieldType
        {
            Title = "Media",
            Icon = IconName.ClipboardList,
            Description = "Files like images, videos, etc",
            Key = "media",
            Order = 9
        });
    }
}
