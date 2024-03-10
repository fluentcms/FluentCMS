namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;


public class PasswordInfo: Base
{
    public static string Name = "Password";
    public static string Description = "Password field with encryption";
    public static Type BasicSettingsView = typeof(TextBasicSettings);
    public static Type AdvancedSettingsView = typeof(PasswordBasicSettings);
    public static Type Preview =typeof(PasswordPreview);
}
