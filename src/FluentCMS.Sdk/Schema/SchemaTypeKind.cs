namespace FluentCMS.Schema;

/// <summary>The kind of a <see cref="SchemaType"/>.</summary>
public enum SchemaTypeKind
{
    /// <summary>A content type with many entries.</summary>
    Collection = 1,

    /// <summary>A content type with a single entry.</summary>
    Singleton = 2,

    /// <summary>A reusable group of fields embedded in another type.</summary>
    Component = 3,

    /// <summary>A member of the union of a blocks field.</summary>
    Block = 4,
}
