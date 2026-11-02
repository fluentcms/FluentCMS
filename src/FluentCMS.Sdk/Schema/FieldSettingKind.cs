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
}
