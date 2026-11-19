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

    /// <summary>
    /// Checks a value that has the right <see cref="Kind"/>, and returns what is wrong with it, such as
    /// <c>has 'x', which is not a MIME type</c>, or <see langword="null"/> when it is valid. Schema validation reports
    /// the message after the setting and the field it belongs to.
    /// </summary>
    public Func<object, string?>? Validate { get; init; }

    /// <summary>The code of the schema error for a value <see cref="Validate"/> refuses; <c>FCMS1211</c> when not set. A plugin gives a code of its own range.</summary>
    public string? ErrorCode { get; init; }
}
