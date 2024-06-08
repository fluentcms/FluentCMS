namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public static class ContentTypeFieldHelper
{
    public static T? GetValue<T>(this ContentTypeField field, string fieldName)
    {
        if (field?.Settings == null || !field.Settings.TryGetValue(fieldName, out object? value))
            return default;

        // check for primitive types and try to parse and return
        if (typeof(T) == typeof(bool) && value is bool boolValue)
            return (T)(object)boolValue;

        if (typeof(T) == typeof(int) && value is int intValue)
            return (T)(object)intValue;

        if (typeof(T) == typeof(decimal) && value is decimal decimalValue)
            return (T)(object)decimalValue;

        if (typeof(T) == typeof(string) && value is string stringValue)
            return (T)(object)stringValue;

        if (typeof(T) == typeof(Guid) && value is Guid guidValue)
            return (T)(object)guidValue;

        if (typeof(T) == typeof(DateTime) && value is DateTime dateTimeValue)
            return (T)(object)dateTimeValue;

        return default;
    }

    public static bool? GetBoolean(this ContentTypeField field, string fieldName)
    {
        return field.GetValue<bool>(fieldName);
    }

    public static string? GetString(this ContentTypeField field, string fieldName)
    {
        return field.GetValue<string>(fieldName);
    }

    public static Decimal? GetDecimal(this ContentTypeField field, string fieldName)
    {
        return field.GetValue<decimal>(fieldName);
    }

    public static DateTime? GetDateTime(this ContentTypeField field, string fieldName)
    {
        return field.GetValue<DateTime>(fieldName);
    }

    public static Guid? GetGuid(this ContentTypeField field, string fieldName)
    {
        return field.GetValue<Guid>(fieldName);
    }
}
