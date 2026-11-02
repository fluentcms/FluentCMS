namespace FluentCMS.Schema;

/// <summary>A setting that a field type accepts in <see cref="SchemaField.Settings"/>.</summary>
/// <param name="Key">The key of the setting, in camelCase.</param>
/// <param name="Kind">The kind of value the setting holds.</param>
/// <param name="Required">Whether every field of the type must set it.</param>
public sealed record FieldSettingDescriptor(string Key, FieldSettingKind Kind, bool Required = false);
