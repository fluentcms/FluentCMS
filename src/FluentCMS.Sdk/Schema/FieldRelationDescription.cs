namespace FluentCMS.Schema;

/// <summary>A <see cref="FieldRelation"/> as plain data.</summary>
public sealed record FieldRelationDescription
{
    /// <summary>The name of the content type the relation points to.</summary>
    public required string Target { get; init; }

    /// <summary>The name of the reverse side on the target type, or <see langword="null"/> if the relation has none.</summary>
    public string? Reverse { get; init; }

    /// <summary>Whether the order of the related entries is kept.</summary>
    public bool Ordered { get; init; }
}
