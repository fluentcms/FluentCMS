namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class TextInfo: Base
{
    public static string Name = "Text";
    public static string Description = "Small or long text like title or description";
    public static Type BasicSettingsView = typeof(TextBasicSettings);
    public static Type AdvancedSettingsView = typeof(TextBasicSettings);
    public static Type Preview = typeof(TextPreview);
}
