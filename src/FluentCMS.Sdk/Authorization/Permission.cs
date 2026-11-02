using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace FluentCMS.Authorization;

/// <summary>
/// A permission a plugin declares so that roles can grant it. A name is two or more segments joined by dots, each
/// lower-case letters, digits and hyphens starting with a letter, such as <c>media.manage</c>. Prefix the name with the
/// plugin's own area; <c>content</c> is reserved for the permissions generated per content type.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Permission is the name the specification gives the type.")]
public sealed partial class Permission
{
    /// <summary>The prefix of the permissions that core generates for each content type.</summary>
    public const string ContentPrefix = "content";

    /// <summary>Creates a permission.</summary>
    /// <param name="name">The name; see <see cref="IsValidName"/>.</param>
    /// <param name="description">What holding the permission allows, shown where roles are edited.</param>
    /// <exception cref="ArgumentException">The name is not valid, or the description is empty.</exception>
    public Permission(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (!IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not a permission name: use two or more lower-case segments joined by dots, such as 'media.manage'.", nameof(name));
        }

        Name = name;
        Description = description;
    }

    /// <summary>The name.</summary>
    public string Name { get; }

    /// <summary>What holding the permission allows.</summary>
    public string Description { get; }

    /// <summary>Checks a permission name.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when the name has at least two segments of lower-case letters, digits and hyphens, each starting with a letter.</returns>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    /// <inheritdoc/>
    public override string ToString() => Name;

    [GeneratedRegex(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
