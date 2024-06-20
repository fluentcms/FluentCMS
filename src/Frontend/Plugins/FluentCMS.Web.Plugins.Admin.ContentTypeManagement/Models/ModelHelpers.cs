using System.Reflection;

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public static class ModelHelpers
{
    public static IFieldValue GetFieldValue(this IFieldModel fieldModel, IDictionary<string, object> valuesDict)
    {
        switch (fieldModel.Type)
        {
            case FieldTypes.STRING:
            case FieldTypes.SINGLE_SELECT:
                return new FieldValue<string?> { Name = fieldModel.Name, Value = (string?)valuesDict[fieldModel.Name] };

            case FieldTypes.NUMBER:
                return new FieldValue<decimal?> { Name = fieldModel.Name, Value = (decimal?)valuesDict[fieldModel.Name] };

            case FieldTypes.SINGLE_FILE:
                return new FieldValue<Guid?> { Name = fieldModel.Name, Value = (Guid?)valuesDict[fieldModel.Name] };

            case FieldTypes.BOOLEAN:
                return new FieldValue<bool> { Name = fieldModel.Name, Value = (bool)(valuesDict[fieldModel.Name] ?? false) };

            case FieldTypes.DATE_TIME:
                return new FieldValue<DateTime?> { Name = fieldModel.Name, Value = (DateTime?)valuesDict[fieldModel.Name] };

            case FieldTypes.MULTI_SELECT:
                return new FieldValue<ICollection<string>?> { Name = fieldModel.Name, Value = (valuesDict[fieldModel.Name] as object[] ?? []).Select(x => x.ToString()).ToList() };

            default:
                throw new NotSupportedException($"Field type '{fieldModel.Type}' is not supported.");
        }
    }
    public static IFieldValue GetFieldValue(this IFieldModel fieldModel)
    {
        switch (fieldModel.Type)
        {
            case FieldTypes.STRING:
            case FieldTypes.SINGLE_SELECT:
                return new FieldValue<string?> { Name = fieldModel.Name };

            case FieldTypes.SINGLE_FILE:
                return new FieldValue<Guid?> { Name = fieldModel.Name };

            case FieldTypes.NUMBER:
                return new FieldValue<decimal?> { Name = fieldModel.Name };

            case FieldTypes.BOOLEAN:
                return new FieldValue<bool> { Name = fieldModel.Name };

            case FieldTypes.DATE_TIME:
                return new FieldValue<DateTime?> { Name = fieldModel.Name };

            case FieldTypes.MULTI_SELECT:
                return new FieldValue<ICollection<string>?> { Name = fieldModel.Name };

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

            if (prop.Name == "FormColWidth" && settingsDict.ContainsKey(prop.Name))
            {
                // try cast to int
                var intValue = int.TryParse(settingsDict["FormColWidth"]?.ToString(), out var _value) ? _value : 12;
                prop.SetValue(result, intValue);
                continue;
            }

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
            FieldTypes.SINGLE_SELECT => src.ToFieldModel<string?, SelectFieldModel>(),
            FieldTypes.SINGLE_FILE => src.ToFieldModel<Guid?, SingleFileFieldModel>(),
            FieldTypes.MULTI_SELECT => src.ToFieldModel<ICollection<string>, MultiSelectFieldModel>(),
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
