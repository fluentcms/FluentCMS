namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class ComponentTypeOption
{
    public string Type { get; set; } = default!;
    public string Text { get; set; } = default!;

    public ComponentTypeOption()
    {
        Type = string.Empty;
        Text = string.Empty;
    }

    public ComponentTypeOption(string type, string text)
    {
        Type = type;
        Text = text;
    }
}
