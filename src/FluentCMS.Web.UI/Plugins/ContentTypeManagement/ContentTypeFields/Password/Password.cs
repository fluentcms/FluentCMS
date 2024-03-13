namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class PasswordInfo: Base
{
    public static string Key = "password";

    public static string Title = "Password";

    public static string Description = "Password field with encryption";

    public static IconName Icon = IconName.Lock;

    public static int Order = 4;

    public static Type BasicSettings = typeof(BasicSettings);

    public static Type AdvancedSettings = typeof(AdvancedSettings);

    public static Type Preview = typeof(PasswordPreview); 
}
