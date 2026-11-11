using System.Collections.Frozen;

namespace FluentCMS.Content;

/// <summary>
/// The identity an operation runs as. Two principals are equal when their kind and id match;
/// the name and claims describe the identity and do not take part in equality.
/// </summary>
public sealed class Principal : IEquatable<Principal>
{
    /// <summary>
    /// The claim that holds the names of the member groups a member is in, separated by spaces. The engine sets it from
    /// the member's groups as they are when the operation is authorized, so a row rule reads the groups of that moment.
    /// </summary>
    public const string GroupsClaim = "fcms_groups";

    private readonly FrozenDictionary<string, string> _claims;

    /// <summary>Creates a principal.</summary>
    /// <param name="kind">The kind of identity. <see cref="PrincipalKind.System"/> and <see cref="PrincipalKind.Anonymous"/> are reserved for <see cref="System"/> and <see cref="Anonymous"/>.</param>
    /// <param name="id">The id of the user, member, API key or service account.</param>
    /// <param name="name">A display name for logs and the activity log.</param>
    /// <param name="claims">Claims by type, for row rules and scopes.</param>
    public Principal(PrincipalKind kind, Guid id, string name, IEnumerable<KeyValuePair<string, string>>? claims = null)
        : this(kind, id, name, claims, allowReserved: false)
    {
    }

    private Principal(PrincipalKind kind, Guid id, string name, IEnumerable<KeyValuePair<string, string>>? claims, bool allowReserved)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown principal kind.");
        }

        if (kind == PrincipalKind.System && !allowReserved)
        {
            throw new ArgumentException("Use Principal.System for the system principal.", nameof(kind));
        }

        if (kind == PrincipalKind.Anonymous && !allowReserved)
        {
            throw new ArgumentException("Use Principal.Anonymous for the anonymous principal.", nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(name);

        Kind = kind;
        Id = id;
        Name = name;
        _claims = (claims ?? []).ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The single system principal, used by jobs, seeding and the command line tool.</summary>
    public static Principal System { get; } = new(PrincipalKind.System, Guid.Empty, "System", null, allowReserved: true);

    /// <summary>The single anonymous principal, a visitor with no identity. It has no claims, so no scopes.</summary>
    public static Principal Anonymous { get; } = new(PrincipalKind.Anonymous, Guid.Empty, "Anonymous", null, allowReserved: true);

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

    /// <summary>Whether this is the anonymous principal.</summary>
    public bool IsAnonymous => Kind == PrincipalKind.Anonymous;

    /// <summary>Gets the value of a claim.</summary>
    /// <param name="type">The claim type.</param>
    /// <returns>The claim value, or <see langword="null"/> when the principal has no such claim.</returns>
    public string? Claim(string type) => _claims.GetValueOrDefault(type);

    /// <summary>Whether the principal is a member of the member group with this name. Only a member is in groups.</summary>
    /// <param name="name">The name of the group, as it was given when the group was made.</param>
    /// <returns><see langword="true"/> when the principal's groups hold the name.</returns>
    public bool IsInGroup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Kind == PrincipalKind.Member
            && name.Length > 0
            && Claim(GroupsClaim) is { } groups
            && groups.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public bool Equals(Principal? other) => other is not null && Kind == other.Kind && Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Principal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Id);

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}:{Id}";
}
