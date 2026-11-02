using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

internal static class SettingValues
{
    public static string? Text(SchemaField field, string key) =>
        field.Settings.TryGetValue(key, out var value) ? value as string : null;

    public static IReadOnlyList<string> List(SchemaField field, string key) =>
        field.Settings.TryGetValue(key, out var value) && value is IEnumerable<string> list ? [.. list] : [];

    public static int? Int(SchemaField field, string key) => field.Settings.TryGetValue(key, out var value)
        ? value switch
        {
            long whole when whole is >= int.MinValue and <= int.MaxValue => (int)whole,
            decimal number when number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue => (int)number,
            _ => null,
        }
        : null;
}
