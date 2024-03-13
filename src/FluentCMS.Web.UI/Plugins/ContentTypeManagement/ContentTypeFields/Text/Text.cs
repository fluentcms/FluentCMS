namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class TextInfo: Base
{
    public static string Key = "text";

    public static string Title = "Text";

    public static string Description = "Small or long text like title or description";

    public static IconName Icon = IconName.Text;

    public static int Order = 1;

    public static Type BasicSettings = typeof(BasicSettings);

    public static Type AdvancedSettings = typeof(AdvancedSettings);

    public static Type Preview = typeof(TextPreview);
}
