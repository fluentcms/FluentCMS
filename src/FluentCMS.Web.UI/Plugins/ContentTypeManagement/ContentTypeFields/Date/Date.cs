namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class DateInfo : Base
{
    public static string Key = "date";

    public static string Title = "Date";

    public static string Description = "A date picker with hours, minutes and seconds";

    public static IconName Icon = IconName.CalendarWeek;

    public static int Order = 7;

    public static Type BasicSettings = typeof(DateBasicSettings);

    public static Type AdvancedSettings = typeof(DateAdvancedSettings);
}
