namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class DateFormatOptions
{
    public string Key { get; set; } = default!;
    public string Text { get; set; } = default!;

    public DateFormatOptions()
    {
        Key = string.Empty;
        Text = string.Empty;
    }

    public DateFormatOptions(string key, string text)
    {
        Key = key;
        Text = text;
    }
}