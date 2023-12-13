namespace FluentCMS.Web.UI.Resources;

// Enum Class for Icons
public class IconDefinition
{
    // home icon
    public static readonly IconDefinition Default = new("Default");
    public static readonly IconDefinition Home = new("Home");
    public static readonly IconDefinition Delete = new("Delete");
    public static readonly IconDefinition Plus = new("Plus");
    public static readonly IconDefinition Search = new("Search");
    public static readonly IconDefinition Settings = new("Settings");
    public static readonly IconDefinition Filter = new("Filter");
    public static readonly IconDefinition InformationFilled = new("InformationFilled");
    
    private IconDefinition(string iconName)
    {
        IconName = iconName;
    }

    public string IconName { get; }
}