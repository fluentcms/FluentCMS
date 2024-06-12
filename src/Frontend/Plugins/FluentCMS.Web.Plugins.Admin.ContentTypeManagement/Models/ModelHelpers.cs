using System.Reflection;

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public static class ModelHelpers
{
    public static IFieldValue GetFieldValue(this IFieldModel fieldModel, IDictionary<string, object> valuesDict)
    {
        switch (fieldModel.Type)
        {
            case FieldTypes.STRING:
                return new FieldValue<string?> { Name = fieldModel.Name, Value = (string?)valuesDict[fieldModel.Name] };

            case FieldTypes.NUMBER:
                return new FieldValue<decimal?> { Name = fieldModel.Name, Value = (decimal?)valuesDict[fieldModel.Name] };

            case FieldTypes.BOOLEAN:
                return new FieldValue<bool> { Name = fieldModel.Name, Value = (bool)(valuesDict[fieldModel.Name] ?? false) };

            case FieldTypes.DATE_TIME:
                return new FieldValue<DateTime?> { Name = fieldModel.Name, Value = (DateTime?)valuesDict[fieldModel.Name] };

            default:
                throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
        }
    }
    public static IFieldValue GetFieldValue(this IFieldModel fieldModel)
    {
        switch (fieldModel.Type)
        {
            case FieldTypes.STRING:
                return new FieldValue<string?> { Name = fieldModel.Name };

            case FieldTypes.NUMBER:
                return new FieldValue<decimal?> { Name = fieldModel.Name };

            case FieldTypes.BOOLEAN:
                return new FieldValue<bool> { Name = fieldModel.Name };

            case FieldTypes.DATE_TIME:
                return new FieldValue<DateTime?> { Name = fieldModel.Name };

            default:
                throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
        }
    }

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
