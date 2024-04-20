namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class EnumerationInfo : Base
{
    public static string Key = "enumeration";

    public static string Title = "Enumeration";

    public static string Description = "List of values, then pick one";

    public static IconName Icon = IconName.List;

    public static int Order = 6;

    public static Type BasicSettings = typeof(EnumerationBasicSettings);

    public static Type AdvancedSettings = typeof(EnumerationAdvancedSettings);
}
