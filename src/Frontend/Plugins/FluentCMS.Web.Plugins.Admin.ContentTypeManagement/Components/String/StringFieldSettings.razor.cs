namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class StringFieldSettings
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
            Key = "StringFieldFormText",
            Text = "Input",
        },
        new()
        {
            Key = "StringFieldFormTextArea",
            Text = "Textarea",
        },
        new()
        {
            Key = "StringFieldFormRichText",
            Text = "Rich Text",
        },
        new()
        {
            Key = "StringFieldFormMarkdownText",
            Text = "Markdown",
        },
    };

    private List<SelectItem> TableViewTypes { get; set; } = new List<SelectItem>
    {
        new()
        {
            Key = "StringFieldDataTableView",
            Text = "Simple Text",
        },
    };
}