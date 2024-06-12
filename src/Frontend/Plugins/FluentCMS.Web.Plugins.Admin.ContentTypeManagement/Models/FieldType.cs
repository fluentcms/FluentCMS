namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;
public class FieldType
{
    public string Title { get; set; } = default!;
    public IconName Icon { get; set; }
    public string Description { get; set; } = default!;
    public string Key { get; set; } = default!;
    public int Order { get; set; }
    public Type SettingViewType { get; set; } = default!;

    public List<Component> FormComponents { get; set; } = default!;
    public List<Component> DataTableComponents { get; set; } = default!;

    public class Component
    {
        public string Name { get; set; } = default!;
        public string Title { get; set; } = default!;
        public Type Type { get; set; } = default!;

        public Component(string title, Type type)
        {
            Type = type;
            Name = type.Name;
            Title = title;
        }
    }
}
