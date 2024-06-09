namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class NumberFieldSettings
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
            Key = "NumberFieldFormInput",
            Text = "Number Input",
        },
        new()
        {
            Key = "NumberFieldFormRange",
            Text = "Range Input",
        },
    };

    private List<SelectItem> TableViewTypes { get; set; } = new List<SelectItem>
    {
        new()
        {
            Key = "NumberFieldDataTableView",
            Text = "Number",
        },
    };
}