namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class EmailInfo : Base
{
    public static string Key = "email";

    public static string Title = "Email";

    public static string Description = "Email field with validations format";

    public static IconName Icon = IconName.AtSign;

    public static int Order = 6;

    public static Type BasicSettings = typeof(FieldBasicSettings);

    public static Type AdvancedSettings = typeof(EmailAdvancedSettings);
}
