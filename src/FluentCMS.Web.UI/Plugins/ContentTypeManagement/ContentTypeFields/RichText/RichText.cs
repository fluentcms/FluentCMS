namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class RichTextInfo : Base
{
    public static string Key = "rich-text";

    public static string Title = "Rich Text";

    public static string Description = "A rich text editor with formatting options";

    public static IconName Icon = IconName.Paragraph;

    public static int Order = 3;

    public static Type BasicSettings = typeof(RichTextBasicSettings);

    public static Type AdvancedSettings = typeof(RichTextAdvancedSettings);
}
