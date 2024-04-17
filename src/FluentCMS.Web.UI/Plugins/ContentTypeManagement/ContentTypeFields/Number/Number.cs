namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class NumberInfo : Base
{
    public static string Key = "number";

    public static string Title = "Number";

    public static string Description = "Numbers (integer, float, decimal)";

    public static IconName Icon = IconName.Number;

    public static int Order = 5;

    public static Type BasicSettings = typeof(NumberBasicSettings);

    public static Type AdvancedSettings = typeof(NumberAdvancedSettings);
}
