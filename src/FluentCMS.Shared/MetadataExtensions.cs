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

        var metaDataDictionary = (IDictionary<string, object?>)metaDataField.GetValue(obj);
        if (metaDataDictionary == null)
        {
            metaDataField.SetValue(obj, new Dictionary<string, object?>());
            metaDataDictionary = (IDictionary<string, object?>)metaDataField.GetValue(obj);
        }
        if (!metaDataDictionary.ContainsKey(fieldName))
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
            metaDataField.SetValue(obj,new Dictionary<string,object?>());
            metaDataDictionary = metaDataField.GetValue(obj);
        }
        ((IDictionary<string, object?>)metaDataDictionary!)[fieldName] = value;
    }

}
