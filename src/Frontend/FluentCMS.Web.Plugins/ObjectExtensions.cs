﻿using System.Reflection;

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

    public static IDictionary<string, object> AsDictionary(this object source, BindingFlags bindingAttr = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
    {
        return source.GetType().GetProperties(bindingAttr).ToDictionary
        (
            propInfo => propInfo.Name,
            propInfo => propInfo.GetValue(source, null) ?? default!
        );
    }
}
