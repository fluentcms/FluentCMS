﻿namespace FluentCMS.Shared;

public static class MetadataExtensions
{
    public static T? GetValue<T>(this object obj, string fieldName, string metadataField = "Metadata")
    {
        var metaDataField = obj.GetType().GetProperty(metadataField);
        if (metaDataField == null || !metaDataField.PropertyType.IsAssignableTo(typeof(IDictionary<string, object?>)))
        {
            throw new Exception("Cant use this type");
        }

        var dictionary = (IDictionary<string, object?>)metaDataField.GetValue(obj);
        if (!dictionary.ContainsKey(fieldName))
        {
            dictionary[fieldName] = default(T);
        }
        return (T?)(dictionary[fieldName]);
    }
    public static void SetValue<T>(this object obj, string fieldName, T? value, string metadataField = "Metadata")
    {
        var metaDataField = obj.GetType().GetProperty(metadataField);
        if (metaDataField == null || !metaDataField.PropertyType.IsAssignableTo(typeof(IDictionary<string, object?>)))
        {
            throw new Exception("Cant use this type");
        }
        ((IDictionary<string, object?>)metaDataField.GetValue(obj)!)[fieldName] = value;
    }

}
