using FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Components.String;
using System.Reflection;

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Components;

public static class DictionaryExtensions
{
    public static T ToFieldModel<T>(this ContentTypeField src) where T : IFieldModel, new()
    {
        T result = new()
        {
            Name = src.Name ?? string.Empty,
            Type = src.Type ?? string.Empty
        };

        PropertyInfo[] properties = typeof(T).GetProperties();

        var settingsDict = src.Settings ?? new Dictionary<string, object?>();

        foreach (PropertyInfo prop in properties)
        {
            if (prop.CanWrite && settingsDict.TryGetValue(prop.Name, out object? value))
                prop.SetValue(result, value);
        }

        return result;
    }

    public static ContentTypeField ToContentTypeField<T>(this T src) where T : IFieldModel
    {
        var result = new ContentTypeField
        {
            Name = src.Name,
            Type = src.Type,
            Settings = new Dictionary<string, object?>()
        };

        PropertyInfo[] properties = typeof(T).GetProperties();

        foreach (PropertyInfo prop in properties)
        {
            if (prop.Name== "Name" || prop.Name == "Type")
                continue;

            result.Settings.Add(prop.Name, prop.GetValue(src));
        }

        return result;
    }
}
