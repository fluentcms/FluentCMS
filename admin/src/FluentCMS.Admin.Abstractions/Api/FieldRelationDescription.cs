namespace FluentCMS.Admin.Api;

/// <summary>The target of a relation field.</summary>
public sealed record FieldRelationDescription
{
    /// <summary>The content type the relation points to.</summary>
    public required string Target { get; init; }

    /// <summary>The name of the reverse side on the target, if any.</summary>
    public string? Reverse { get; init; }

    /// <summary>Whether the related entries keep the order they were given in.</summary>
    public bool Ordered { get; init; }
}
