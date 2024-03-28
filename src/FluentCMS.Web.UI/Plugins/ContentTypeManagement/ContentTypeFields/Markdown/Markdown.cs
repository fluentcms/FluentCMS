namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class MarkdownInfo : Base
{
    public static string Key = "rich-text-markdown";

    public static string Title = "Rich Text (Markdown)";

    public static string Description = "The classic rich text editor";

    public static IconName Icon = IconName.Paragraph;

    public static int Order = 8;

    public static Type BasicSettings = typeof(MarkdownBasicSettings);

    public static Type AdvancedSettings = typeof(MarkdownAdvancedSettings);
}
