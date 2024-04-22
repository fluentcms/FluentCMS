using System.Reflection;

namespace FluentCMS.Shared;

public static class MetadataExtensions
{
    public static T? GetValue<T>(this object obj, string fieldName, string metadataField = "Metadata")
    {
        var metaDataField = obj.GetType().GetProperty(metadataField);
        if (metaDataField == null || !metaDataField.PropertyType.IsAssignableTo(typeof(IDictionary<string, object?>)))
        {
            throw new Exception("Cant use this type");
        }

        var metaDataDictionary = metaDataField.GetValue(obj) as IDictionary<string, object?>;
        if (metaDataDictionary == null)
        {
            metaDataField.SetValue(obj, new Dictionary<string, object?>());
            metaDataDictionary = metaDataField.GetValue(obj) as IDictionary<string, object?>;
        }
        if (!metaDataDictionary!.ContainsKey(fieldName))
        {
            metaDataDictionary[fieldName] = default(T);
        }
        return (T?)(metaDataDictionary[fieldName]);
    }
    public static void SetValue<T>(this object obj, string fieldName, T? value, string metadataField = "Metadata")
    {
        var metaDataField = obj.GetType().GetProperty(metadataField);
        if (metaDataField == null || !metaDataField.PropertyType.IsAssignableTo(typeof(IDictionary<string, object?>)))
        {
            throw new Exception("Cant use this type");
        }

        var metaDataDictionary = metaDataField.GetValue(obj);
        if (metaDataDictionary == null)
        {
            metaDataField.SetValue(obj, new Dictionary<string, object?>());
            metaDataDictionary = metaDataField.GetValue(obj);
        }
        ((IDictionary<string, object?>)metaDataDictionary!)[fieldName] = value;
    }
    public static IEnumerable<T>? GetValueArray<T>(this object obj, string fieldName, string metadataField = "Metadata")
    {
        var metaDataField = obj.GetType().GetProperty(metadataField) ?? throw new Exception("Type Does not have a meta data field");
        var metaDataDictionary = (metaDataField.GetValue(obj) as IDictionary<string, object?>) ?? new Dictionary<string, object?>();
        var v = metaDataDictionary[fieldName] as IEnumerable<object?> ?? Array.Empty<object?>();
        foreach (var item in v)
        {
            yield return ((T?)item) ?? throw new Exception("Couldnt cast item to T");
        }
    }
}
