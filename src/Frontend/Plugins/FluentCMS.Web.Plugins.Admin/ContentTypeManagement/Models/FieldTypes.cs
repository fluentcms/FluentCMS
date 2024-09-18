namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class FieldTypes
{
    public const string STRING = "string";
    public const string NUMBER = "number";
    public const string BOOLEAN = "bool";
    public const string SINGLE_SELECT = "single-select";
    public const string MULTI_SELECT = "multi-select";
    public const string DATE_TIME = "datetime";
    public const string SINGLE_FILE = "single-file";

    public static Type GetSettingType(string fieldTypeKey) =>
        All[fieldTypeKey].SettingViewType;

    public static readonly Dictionary<string, FieldType> All = new()
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
                Order = 6,
                SettingViewType = typeof(MultiSelectFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Checkboxes", typeof(MultiSelectFieldFormCheckboxes)),
                    new FieldType.Component("Autocomplete", typeof(MultiSelectFieldFormAutocomplete))

                ],
                DataTableComponents =
                [
                    new FieldType.Component("Badges", typeof(MultiSelectFieldDataTableBadges)),
                ]
            }
        },
        {
            SINGLE_SELECT,
            new FieldType
            {
                Title = "Single Select",
                Icon = IconName.List,
                Description = "List of values, then pick one",
                Key = SINGLE_SELECT,
                Order = 5,
                SettingViewType = typeof(SelectFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Dropdown", typeof(SelectFieldFormSelect)),
                    new FieldType.Component("Radio Group", typeof(SelectFieldFormRadioGroup)),
                    new FieldType.Component("Autocomplete", typeof(SelectFieldFormAutocomplete))
                ],
                DataTableComponents =
                [
                    new FieldType.Component("Badge", typeof(SelectFieldDataTableBadge)),
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
                Order = 4,
                SettingViewType = typeof(DateFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("Date Picker", typeof(DateFieldFormDate)),
                    new FieldType.Component("Time Picker", typeof(DateFieldFormTime)),
                    new FieldType.Component("DateTime Picker", typeof(DateFieldFormDateTime)),
                ],
                DataTableComponents =
                [
                    new FieldType.Component("Text", typeof(DateFieldDataTableView)),
                ]
            }
        },
        {
            SINGLE_FILE,
            new FieldType
            {
                Title = "Single File",
                Icon = IconName.ClipboardList,
                Description = "Files like images, videos, etc",
                Key = SINGLE_FILE,
                Order = 7,
                SettingViewType = typeof(SingleFileFieldSettings),
                FormComponents =
                [
                    new FieldType.Component("File Picker", typeof(SingleFileFieldFormFilePicker)),
                    // new FieldType.Component("Time Picker", typeof(DateFieldFormTime)),
                    // new FieldType.Component("DateTime Picker", typeof(DateFieldFormDateTime)),
                ],
                DataTableComponents =
                [
                    new FieldType.Component("File", typeof(SingleFileFieldDataTableFile)),

                    // new FieldType.Component("Text", typeof(DateFieldDataTableView)),
                ]
            }
        }
    };
}
