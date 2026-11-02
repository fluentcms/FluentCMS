using System.Collections.Frozen;

namespace FluentCMS.Content;

/// <summary>
/// The identity an operation runs as. Two principals are equal when their kind and id match;
/// the name and claims describe the identity and do not take part in equality.
/// </summary>
public sealed class Principal : IEquatable<Principal>
{
    private readonly FrozenDictionary<string, string> _claims;

    /// <summary>Creates a principal.</summary>
    /// <param name="kind">The kind of identity. <see cref="PrincipalKind.System"/> is reserved for <see cref="System"/>.</param>
    /// <param name="id">The id of the user, member, API key or service account.</param>
    /// <param name="name">A display name for logs and the activity log.</param>
    /// <param name="claims">Claims by type, for row rules and scopes.</param>
    public Principal(PrincipalKind kind, Guid id, string name, IEnumerable<KeyValuePair<string, string>>? claims = null)
        : this(kind, id, name, claims, allowSystem: false)
    {
    }

    private Principal(PrincipalKind kind, Guid id, string name, IEnumerable<KeyValuePair<string, string>>? claims, bool allowSystem)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown principal kind.");
        }

        if (kind == PrincipalKind.System && !allowSystem)
        {
            throw new ArgumentException("Use Principal.System for the system principal.", nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(name);

        Kind = kind;
        Id = id;
        Name = name;
        _claims = (claims ?? []).ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The single system principal, used by jobs, seeding and the command line tool.</summary>
    public static Principal System { get; } = new(PrincipalKind.System, Guid.Empty, "System", null, allowSystem: true);

    /// <summary>The kind of identity.</summary>
    public PrincipalKind Kind { get; }

    /// <summary>The id of the identity. <see cref="Guid.Empty"/> for the system principal.</summary>
    public Guid Id { get; }

    /// <summary>A display name for logs and the activity log.</summary>
    public string Name { get; }

    /// <summary>The claims by type.</summary>
    public IReadOnlyDictionary<string, string> Claims => _claims;

    /// <summary>Whether this is the system principal.</summary>
    public bool IsSystem => Kind == PrincipalKind.System;

    /// <summary>Gets the value of a claim.</summary>
    /// <param name="type">The claim type.</param>
    /// <returns>The claim value, or <see langword="null"/> when the principal has no such claim.</returns>
    public string? Claim(string type) => _claims.GetValueOrDefault(type);

    /// <inheritdoc/>
    public bool Equals(Principal? other) => other is not null && Kind == other.Kind && Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Principal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Id);

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}:{Id}";
}
