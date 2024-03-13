namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class Base
{
    public string Key { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public IconName Icon { get; set; }

    public int Order { get; set; }

    public Type BasicSettings { get; set; }

    public Type AdvancedSettings { get; set; }

    public Type Preview { get; set; }
}
