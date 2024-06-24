using System.Reflection;

namespace FluentCMS.Web.Plugins;

public static class ObjectExtensions
{
    public static T ToObject<T>(this IDictionary<string, object> source) where T : class, new()
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

    public static Dictionary<string, object> AsDictionary(this object source)
    {        
        return source.GetType().GetProperties(_bindingAttr).ToDictionary
        (
            propInfo => propInfo.Name,
            propInfo => propInfo.GetValue(source, null) ?? default!
        );
    }
}
