namespace FluentCMS.Web.UI.Resources;

// Enum Class for Icons
public class IconDefinition
{
    // home icon
    public static readonly IconDefinition Home = new IconDefinition("home");
    private IconDefinition(string iconName)
    {
        IconName = iconName;
    }

    public string IconName { get; }
}
