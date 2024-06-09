namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class BooleanFieldSettings
{
    class SelectItem
    {
        public string Key { get; set; }
        public string Text { get; set; }
    };

    private List<SelectItem> FormViewTypes { get; set; } = new List<SelectItem>
    {
        new()
        {
            Key = "BooleanFieldFormCheckbox",
            Text = "Checkbox",
        },
        new()
        {
            Key = "BooleanFieldFormSwitch",
            Text = "Switch",
        },
    };

    private List<SelectItem> TableViewTypes { get; set; } = new List<SelectItem>
    {
        new()
        {
            Key = "BooleanFieldDataTableIndicator",
            Text = "Indicator",
        },
        new()
        {
            Key = "BooleanFieldDataTableTrueFalse",
            Text = "True / False",
        },
        new()
        {
            Key = "BooleanFieldDataTableYesNo",
            Text = "Yes / No",
        },
        new()
        {
            Key = "BooleanFieldDataTableSwitch",
            Text = "Switch",
        },
    };
}