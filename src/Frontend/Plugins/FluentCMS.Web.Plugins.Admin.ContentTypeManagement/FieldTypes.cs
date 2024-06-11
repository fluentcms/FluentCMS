namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class FieldType
{
    public string Title { get; set; } = default!;
    public IconName Icon { get; set; }
    public string Description { get; set; } = default!;
    public string Key { get; set; } = default!;
    public int Order { get; set; }
    public Type SettingViewType { get; set; } = default!;
    public Type SettingViewModel { get; set; } = default!;
}

public class FieldTypes : Dictionary<string, FieldType>
{
    public const string STRING = "string";
    public const string NUMBER = "number";
    public const string BOOLEAN = "bool";
    public const string SELECT = "select";
    public const string DATE = "date";
    public const string MEDIA = "media";

    public FieldTypes()
    {
        Add(STRING, new FieldType
        {
            Icon = IconName.Text,
            Title = "String",
            Description = "Small or long text like title or description",
            Key = STRING,
            Order = 1,
            SettingViewType = typeof(StringFieldSettings),
            SettingViewModel = typeof(StringFieldModel)
        });
        Add(NUMBER, new FieldType
        {
            Title = "Number",
            Icon = IconName.Number,
            Description = "Numbers (integer, float, decimal)",
            Key = NUMBER,
            Order = 2,
            SettingViewType = typeof(NumberFieldSettings),
            SettingViewModel = typeof(NumberFieldModel)
        });
        Add(BOOLEAN, new FieldType
        {
            Title = "Boolean",
            Icon = IconName.Boolean,
            Description = "Yes or no, 1 or 0, true or false",
            Key = BOOLEAN,
            Order = 3,
            SettingViewType = typeof(BooleanFieldSettings),
            SettingViewModel = typeof(BooleanFieldModel)
        });
        Add(SELECT, new FieldType
        {
           Title = "Select",
           Icon = IconName.List,
           Description = "List of values, then pick one",
           Key = SELECT,
           Order = 4
        });
        Add(DATE, new FieldType
        {
            Title = "Date",
            Icon = IconName.Clock,
            Description = "A date picker with hours, minutes and seconds",
            Key = DATE,
            Order = 5,
            SettingViewType = typeof(DateFieldSettings),
            SettingViewModel = typeof(DateFieldModel)
        });
        //Add(MEDIA, new FieldType
        //{
        //    Title = "Media",
        //    Icon = IconName.ClipboardList,
        //    Description = "Files like images, videos, etc",
        //    Key = MEDIA,
        //    Order = 6
        //});
    }
}
