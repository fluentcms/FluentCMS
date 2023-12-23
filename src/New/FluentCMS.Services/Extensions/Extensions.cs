using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Services.Extensions;
public static class Extensions
{
    public static T Merge<T>(this T target, T source)
    {
        var type = typeof(T);
        var properties = type.GetProperties();
        foreach (var property in properties)
        {
            // ignore if null
            if (property.GetValue(source) == null) continue;
            property.SetValue(target, property.GetValue(source));
        }
        return target;
    }

    public static (T[] Removed, T[]Added) Diff<T>(this IEnumerable<T> source, IEnumerable<T> target)
    {
        var removed = source.Except(target);
        var added = target.Except(removed);
        return (removed.ToArray(), added.ToArray());
    }
}
