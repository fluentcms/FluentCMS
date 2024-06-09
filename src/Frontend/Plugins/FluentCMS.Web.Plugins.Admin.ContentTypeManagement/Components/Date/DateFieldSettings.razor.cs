namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class DateFieldSettings
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
            Key = "DateFieldFormInput",
            Text = "Date Input",
        },
    };

    private List<SelectItem> TableViewTypes { get; set; } = new List<SelectItem>
    {
        new()
        {
            Key = "DateFieldDataTableView",
            Text = "Date String",
        }
    };
}