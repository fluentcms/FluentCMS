namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class FieldType
{
    public string Title { get; set; } = default!;
    public IconName Icon { get; set; }
    public string Description { get; set; } = default!;
    public string Key { get; set; } = default!;
    public int Order { get; set; }
    public Type SettingViewType { get; set; } = default!;

    public List<Component> FormComponents { get; set; } = default!;
    public List<Component> DataTableComponents { get; set; } = default!;

    public class Component
    {
        public string Name { get; set; } = default!;
        public string Title { get; set; } = default!;
        public Type Type { get; set; } = default!;

        public Component(string title, Type type)
        {
            Type = type;
            Name = type.Name;
            Title = title;
        }
    }
}

public class FieldTypes
{
    public const string STRING = "string";
    public const string NUMBER = "decimal";
    public const string BOOLEAN = "bool";
    public const string ENUMERATION = "enumeration";
    public const string DATE_TIME = "datetime";
    public const string MEDIA = "media";

    public static Type GetSettingType(string fieldTypeKey) =>
        All[fieldTypeKey].SettingViewType;

    public static Dictionary<string, FieldType> All = new()
    {
        {
            STRING,
            new FieldType
            {
                Icon = IconName.Text,
                Title = "String",
                Description = "Small or long text like title or description",
                Key = STRING,
                Order = 1,
                SettingViewType = typeof(StringFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Input", typeof(StringFieldFormText)),
                    new FieldType.Component("TextArea", typeof(StringFieldFormTextArea)),
                    new FieldType.Component("Rich Text Editor", typeof(StringFieldFormRichText)),
                    new FieldType.Component("Markdown Editor", typeof(StringFieldFormMarkdownText))
                ],
                DataTableComponents =
                [
                    new FieldType.Component("Raw Text", typeof(StringFieldDataTableView))
                ]
            }
        },
        {
            NUMBER,
            new FieldType
            {
                Title = "Number",
                Icon = IconName.Number,
                Description = "Numbers (integer, float, decimal)",
                Key = NUMBER,
                Order = 2,
                SettingViewType = typeof(NumberFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Number Input", typeof(NumberFieldFormInput)),
                    new FieldType.Component("Range Input", typeof(NumberFieldFormRange)),
                ],
                DataTableComponents =
                [
                    new FieldType.Component("Raw Text", typeof(NumberFieldDataTableView))
                ]
            }
        },
        {
            BOOLEAN,
            new FieldType
            {
                Title = "Boolean",
                Icon = IconName.Boolean,
                Description = "Yes or no, 1 or 0, true or false",
                Key = BOOLEAN,
                Order = 3,
                SettingViewType = typeof(BooleanFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Switch", typeof(BooleanFieldFormSwitch)),
                    new FieldType.Component("Checkbox", typeof(BooleanFieldFormCheckbox)),
                ],
                DataTableComponents =
                [
                    new FieldType.Component("True/False", typeof(BooleanFieldDataTableTrueFalse)),
                    new FieldType.Component("Yes/No", typeof(BooleanFieldDataTableYesNo)),
                    new FieldType.Component("Switch", typeof(BooleanFieldDataTableSwitch)),
                    new FieldType.Component("Indicator", typeof(BooleanFieldDataTableIndicator)),
                ]
            }
        },
        //{ENUMERATION, new FieldType
        //{
        //    Title = "Enumeration",
        //    Icon = IconName.List,
        //    Description = "List of values, then pick one",
        //    Key = ENUMERATION,
        //    Order = 4
        //}},
        {
            DATE_TIME,
            new FieldType
            {
                Title = "Date Time",
                Icon = IconName.Clock,
                Description = "A date picker with hours, minutes and seconds",
                Key = DATE_TIME,
                Order = 5,
                SettingViewType = typeof(DateFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Input", typeof(DateFieldFormInput))
                ],
                DataTableComponents =
                [
                    new FieldType.Component("Text", typeof(DateFieldDataTableView)),
                ]
            }
        }
        //{MEDIA, new FieldType
        //{
        //    Title = "Media",
        //    Icon = IconName.ClipboardList,
        //    Description = "Files like images, videos, etc",
        //    Key = MEDIA,
        //    Order = 6
        //}}
    };
}
