namespace FluentCMS.Schema;

/// <summary>How the value of a field is stored.</summary>
public enum FieldStorageKind
{
    /// <summary>In a column of the content table of its type.</summary>
    Column,

    /// <summary>As JSON in a single column.</summary>
    Json,

    /// <summary>As rows of the reference table, with no column of its own.</summary>
    Reference,
}
