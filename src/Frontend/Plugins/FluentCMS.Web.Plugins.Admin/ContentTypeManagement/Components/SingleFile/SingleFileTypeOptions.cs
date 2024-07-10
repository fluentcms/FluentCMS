namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class SingleFileTypeOptions
{
    public string Key { get; set; } = default!;
    public string Text { get; set; } = default!;

    public SingleFileTypeOptions()
    {
        Key = string.Empty;
        Text = string.Empty;
    }

    public SingleFileTypeOptions(string key, string text)
    {
        Key = key;
        Text = text;
    }
}