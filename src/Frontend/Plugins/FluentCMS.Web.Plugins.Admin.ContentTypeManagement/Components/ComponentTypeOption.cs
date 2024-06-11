namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class ComponentTypeOption
{
    public string Name { get; set; } = default!;
    public Type Type { get; set; } = default!;
    public string Text { get; set; } = default!;

    public ComponentTypeOption(Type type, string text)
    {
        Type = type;
        Text = text;
        Name = type.Name;
    }
}
