namespace FluentCMS.Schema;

/// <summary>The target of a relation field.</summary>
public sealed class FieldRelation
{
    internal FieldRelation()
    {
    }

    /// <summary>The name of the content type the relation points to.</summary>
    public string Target { get; internal init; } = "";

    /// <summary>The name of the reverse side on the target type, or <see langword="null"/> if the relation has none.</summary>
    public string? Reverse { get; internal init; }

    /// <summary>Whether the order of the related entries is kept.</summary>
    public bool Ordered { get; internal init; }
}
