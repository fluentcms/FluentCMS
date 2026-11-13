using System.Collections.Frozen;

namespace FluentCMS.Content;

/// <summary>
/// The identity an operation runs as. Two principals are equal when their kind and id match;
/// the name and claims describe the identity and do not take part in equality.
/// </summary>
public sealed class Principal : IEquatable<Principal>
{
    /// <summary>
    /// The claim that holds the names of the member groups a member is in, separated by spaces. The engine sets it, from
    /// the member's groups as they are when the operation is authorized, on the principal a row rule is given, and
    /// nowhere else: the principal of an operation context outside a row rule does not carry it.
    /// </summary>
    public const string GroupsClaim = "fcms_groups";

    private readonly FrozenDictionary<string, string> _claims;

    // Only the principal the engine gives a row rule carries the member groups, the empty set included.
    private readonly bool _carriesGroups;

    /// <summary>Creates a principal.</summary>
    /// <param name="kind">The kind of identity. <see cref="PrincipalKind.System"/> and <see cref="PrincipalKind.Anonymous"/> are reserved for <see cref="System"/> and <see cref="Anonymous"/>.</param>
    /// <param name="id">The id of the user, member, API key or service account.</param>
    /// <param name="name">A display name for logs and the activity log.</param>
    /// <param name="claims">Claims by type, for row rules and scopes.</param>
    public Principal(PrincipalKind kind, Guid id, string name, IEnumerable<KeyValuePair<string, string>>? claims = null)
        : this(kind, id, name, claims, allowReserved: false)
    {
    }

    private Principal(PrincipalKind kind, Guid id, string name, IEnumerable<KeyValuePair<string, string>>? claims, bool allowReserved, bool carriesGroups = false)
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
        _carriesGroups = carriesGroups;
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

    /// <summary>
    /// Whether the principal is a member of the member group with this name. Only a member is in groups. This is for row
    /// rules only: the engine reads a member's groups when it authorizes the operation and puts them on the principal it
    /// gives a row rule, and on no other. On any other principal, such as the one of the operation context that a hook,
    /// an event handler, a job or a plugin's endpoint reads, this throws instead of answering <see langword="false"/>,
    /// which would let a check that refuses by group pass.
    /// </summary>
    /// <param name="name">The name of the group, as it was given when the group was made.</param>
    /// <returns><see langword="true"/> when the principal is a member and its groups hold the name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The call is outside a row rule, on a principal that does not carry the groups.</exception>
    public bool IsInGroup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_carriesGroups)
        {
            throw new InvalidOperationException(
                "Principal.IsInGroup was called outside a row rule. Member groups are read inside a row rule only: the engine "
                + "puts them on the principal it gives a row rule (cms.Authorization.AddRowRule), and the principal of an "
                + "operation context, which a hook, an event handler, a job or an endpoint reads, does not carry them.");
        }

        return Kind == PrincipalKind.Member
            && name.Length > 0
            && Claim(GroupsClaim) is { } groups
            && groups.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);
    }

    // For the engine: this principal as a row rule is given it, with the names of the member groups it is in now. A claim
    // of that name it had is dropped, and any kind other than a member is in no group.
    internal Principal InRowRule(IEnumerable<string> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var names = Kind == PrincipalKind.Member ? string.Join(' ', groups) : "";
        var claims = _claims.Where(claim => claim.Key != GroupsClaim);
        return new Principal(
            Kind,
            Id,
            Name,
            names.Length == 0 ? claims : claims.Append(KeyValuePair.Create(GroupsClaim, names)),
            allowReserved: true,
            carriesGroups: true);
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
