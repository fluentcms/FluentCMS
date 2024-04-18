namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class MediaInfo : Base
{
    public static string Key = "media";

    public static string Title = "Media";

    public static string Description = "Files like images, videos, etc";

    public static IconName Icon = IconName.Eye;

    public static int Order = 8;

    public static Type BasicSettings = typeof(MediaBasicSettings);

    public static Type AdvancedSettings = typeof(MediaAdvancedSettings);
}
