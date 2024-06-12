namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class FieldTypes
{
    public const string STRING = "string";
    public const string NUMBER = "decimal";
    public const string BOOLEAN = "bool";
    public const string ENUMERATION = "enumeration";
    public const string MULTI_SELECT = "multi-select";
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
        {
            MULTI_SELECT,
            new FieldType
            {
                Title = "Multi Select",
                Icon = IconName.List,
                Description = "List of values, then pick one or more",
                Key = MULTI_SELECT,
                Order = 4,
                SettingViewType = typeof(MultiSelectFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Checkboxes", typeof(MultiSelectFieldFormCheckboxes))
                ],
                DataTableComponents =
                [
                    new FieldType.Component("Badges", typeof(MultiSelectFieldDataTableBadges)),
                ]
            }
        },
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
