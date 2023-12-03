using System.Reflection;

namespace FluentCMS.Entities;

public static class Helpers
{
    public static Content ToContent(this Dictionary<string, object?> dict)
    {
        var content = new Content();

        // Iterate over the dictionary and map each key-value pair to the corresponding property
        foreach (var kvp in dict)
        {
            switch (kvp.Key.ToLower())
            {
                case "":
                    content.Id = Guid.Parse(kvp.Value?.ToString() ?? Guid.Empty.ToString());
                    break;
                case "createdby":
                    content.CreatedBy = kvp.Value?.ToString() ?? string.Empty;
                    break;
                case "createdat":
                    content.CreatedAt = DateTime.TryParse(kvp.Value?.ToString(), out DateTime createdAt) ? createdAt : default;
                    break;
                case "lastupdatedby":
                    content.LastUpdatedBy = kvp.Value?.ToString() ?? string.Empty;
                    break;
                case "lastupdatedat":
                    content.LastUpdatedAt = DateTime.TryParse(kvp.Value?.ToString(), out DateTime lastUpdatedAt) ? lastUpdatedAt : default;
                    break;
                case "type":
                    content.Type = kvp.Value?.ToString() ?? string.Empty;
                    break;
                case "siteid":
                    content.SiteId = Guid.Parse(kvp.Value?.ToString() ?? Guid.Empty.ToString());
                    break;
                default:
                    content.Add(kvp.Key, kvp.Value);
                    break;
            }
        }

        return content;
    }

    public static Dictionary<string, object?> ToDictionary(this Content content)
    {
        var result = new Dictionary<string, object?>();

        // Using reflection to get properties of the Content class
        foreach (PropertyInfo prop in typeof(Content).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            result[prop.Name] = prop.GetValue(content);
        }

        // Adding the dictionary entries
        foreach (var kvp in content)
        {
            // This will overwrite any property with the same name as a dictionary key
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }
}
