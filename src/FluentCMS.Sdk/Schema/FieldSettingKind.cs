using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Schema;

/// <summary>The kind of value a setting of a field type holds.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The members name the kinds of value a setting holds.")]
public enum FieldSettingKind
{
    /// <summary>A string.</summary>
    String,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>A number, whole or not.</summary>
    Decimal,

    /// <summary>A list of strings.</summary>
    StringList,

    /// <summary>
    /// The name of another field of the same type, component or block, such as the field a value is derived from. Schema
    /// validation refuses one that names no sibling, and schema introspection leaves the setting out for a caller who cannot
    /// read that field. A plugin's field type cannot make one required.
    /// </summary>
    FieldReference,
}
