namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement.ContentTypeFields;

public class ContentTypeFieldExplorer
{
    private static List<Base> types = new();

    public static List<Base> Load()
    {
        if (types.Count > 0) return types;

        types = AppDomain
                .CurrentDomain
                .GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(Base).IsAssignableFrom(t) && t.Name != "Base")
                .Select(f => new Base()
                {
                    Key = (string)f.GetField("Key").GetValue(null),
                    Title = (string)f.GetField("Title").GetValue(null),
                    Description = (string)f.GetField("Description").GetValue(null),
                    Icon = (IconName)f.GetField("Icon").GetValue(null),
                    Order = (int)f.GetField("Order").GetValue(null),
                    BasicSettings = (Type)f.GetField("BasicSettings").GetValue(null),
                    AdvancedSettings = (Type)f.GetField("AdvancedSettings").GetValue(null),
                }
                )
                .OrderBy(f => f.Order)
                .ToList();

        return types;
    }
    public static Base? Find(string? key)
    {
        return Load().FirstOrDefault(x => x.Key == key);
    }
}
