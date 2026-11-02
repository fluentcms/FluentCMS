namespace FluentCMS.Sites;

/// <summary>The id of a site.</summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct SiteId(Guid Value)
{
    /// <summary>The default value, which does not identify any site.</summary>
    public static SiteId Empty => default;

    /// <summary>Whether this is <see cref="Empty"/>.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
