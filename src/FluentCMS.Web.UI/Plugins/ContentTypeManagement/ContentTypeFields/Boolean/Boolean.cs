namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class BooleanInfo : Base
{
    public static string Key = "boolean";

    public static string Title = "Boolean";

    public static string Description = "Yes or no, 1 or 0, true or false";

    public static IconName Icon = IconName.Boolean;

    public static int Order = 2;

    public static Type BasicSettings = typeof(BooleanBasicSettings);

    public static Type AdvancedSettings = typeof(BooleanAdvancedSettings);
}
