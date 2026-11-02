namespace FluentCMS.Schema;

/// <summary>A setting that a field type accepts in <see cref="SchemaField.Settings"/>.</summary>
public sealed record FieldSettingDescriptor
{
    /// <summary>The key of the setting, in camelCase.</summary>
    public required string Key { get; init; }

    /// <summary>The kind of value the setting holds.</summary>
    public required FieldSettingKind Kind { get; init; }

    /// <summary>Whether every field of the type must set it.</summary>
    public bool Required { get; init; }
}
