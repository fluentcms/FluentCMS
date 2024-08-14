using System.Reflection;

namespace FluentCMS.Web.ApiClients;

public static class ObjectExtensions
{
    public static List<T> ToContentList<T>(this ICollection<PluginContentDetailResponse> items) where T : IContent, new()
    {
        var result = new List<T>();
        foreach (var item in items)
        {
            if (item.Data != null)
            {
                var model = item.Data.ToContent<T>();
                model.Id = item.Id;
                result.Add(model);
            }

        }
        return result;
    }

    public static T ToContent<T>(this Dictionary<string, object?> source) where T : IContent, new()
    {
        var someObject = new T();
        var someObjectType = someObject.GetType();

        if (someObjectType != null)
        {
            foreach (var item in source)
            {
                someObjectType.GetProperty(item.Key)?.SetValue(someObject, item.Value, null);
            }
        }
        return someObject;
    }

    private const BindingFlags _bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty;

    public static Dictionary<string, object> ToDictionary(this IContent source)
    {
        var result = new Dictionary<string, object>();
        foreach (var prop in source.GetType().GetProperties(_bindingAttr))
        {
            var propValue = prop.GetValue(source, null);
            if (propValue != null)
            {
                result.Add(prop.Name, propValue);
            }
        }

        result.Remove("Id");
        result.Remove("id");

        return result;
    }

    public static T ToPluginSettings<T>(this Dictionary<string, string> source) where T : IPluginSettings, new()
    {
        var someObject = new T();
        var someObjectType = someObject.GetType();

        if (someObjectType != null)
        {
            foreach (var item in source)
            {
                someObjectType.GetProperty(item.Key)?.SetValue(someObject, item.Value, null);
            }
        }
        return someObject;
    }

    public static Dictionary<string, string> ToDictionary(this IPluginSettings source)
    {
        var result = new Dictionary<string, string>();
        foreach (var prop in source.GetType().GetProperties(_bindingAttr))
        {
            var propValue = prop.GetValue(source, null);
            if (propValue != null)
            {
                result.Add(prop.Name, propValue.ToString());
            }
        }
        return result;
    }
}
