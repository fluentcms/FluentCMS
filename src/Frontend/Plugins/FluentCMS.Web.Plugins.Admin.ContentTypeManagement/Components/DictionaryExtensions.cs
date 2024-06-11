﻿using System.Reflection;

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public static class DictionaryExtensions
{
    private static TField ToFieldModel<T, TField>(this ContentTypeField src) where TField : IFieldModel<T>, new()
    {
        TField result = new()
        {
            Name = src.Name ?? string.Empty
        };

        PropertyInfo[] properties = typeof(TField).GetProperties();

        var settingsDict = src.Settings ?? new Dictionary<string, object?>();

        foreach (PropertyInfo prop in properties)
        {
            if (prop.Name == "Name" || prop.Name == "Type")
                continue;

            if (prop.CanWrite && settingsDict.TryGetValue(prop.Name, out object? value) && value != null)
                prop.SetValue(result, value);
        }

        return result;
    }

    public static IFieldModel ToFieldModel(this ContentTypeField src)
    {
        var typeName = src.Type ??
            throw new ArgumentNullException(nameof(src.Type));

        return typeName switch
        {
            FieldTypes.STRING => src.ToFieldModel<string?, StringFieldModel>(),
            FieldTypes.NUMBER => src.ToFieldModel<decimal?, NumberFieldModel>(),
            FieldTypes.BOOLEAN => src.ToFieldModel<bool, BooleanFieldModel>(),
            FieldTypes.DATE_TIME => src.ToFieldModel<DateTime?, DateFieldModel>(),
            _ => throw new NotSupportedException($"Field type '{typeName}' is not supported."),
        };
    }

    public static ContentTypeField ToContentTypeField<T, TField>(this TField src) where TField : IFieldModel<T>
    {
        var result = new ContentTypeField
        {
            Name = src.Name,
            Type = src.Type,
            Settings = new Dictionary<string, object?>()
        };

        PropertyInfo[] properties = typeof(TField).GetProperties();

        foreach (PropertyInfo prop in properties)
        {
            if (prop.Name == "Name" || prop.Name == "Type")
                continue;

            result.Settings.Add(prop.Name, prop.GetValue(src));
        }

        return result;
    }
}
