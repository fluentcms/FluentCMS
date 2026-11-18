using System.Diagnostics.CodeAnalysis;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Plugins;

namespace FluentCMS.Admin.Lists;

/// <summary>
/// The list cells by field type, which the generated lists draw their columns with. A field type without a cell of its own
/// is shown by <see cref="TextListCell"/> when its value reads as text, and is offered as no column otherwise. The cells of
/// an admin half that is not loaded yet are added with <c>AddLazy</c> and found with <c>CellForAsync</c>, which loads the half.
/// </summary>
public sealed class ListCellRegistry
{
    private readonly Dictionary<string, Registration> _cells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LazyFieldComponent> _lazy = new(StringComparer.Ordinal);
    private readonly HashSet<string> _populated = new(StringComparer.Ordinal);

    /// <summary>Registers the cell of a field type, in place of the one before.</summary>
    /// <typeparam name="TCell">The cell component.</typeparam>
    /// <param name="fieldType">The field type, as <see cref="SchemaFieldDescription.Type"/> names it.</param>
    public void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCell>(string fieldType)
        where TCell : ListCellBase
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        _lazy.Remove(fieldType);
        _populated.Remove(fieldType);
        _cells[fieldType] = new Registration(typeof(TCell));
    }

    /// <summary>
    /// Registers the cell of a field type that an admin half provides, in place of the one before. While
    /// <paramref name="isAvailable"/> answers <see langword="false"/>, as when the half's plugin is disabled in the site,
    /// the type has no cell of its own.
    /// </summary>
    /// <param name="fieldType">The field type, as <see cref="SchemaFieldDescription.Type"/> names it.</param>
    /// <param name="isAvailable">Whether the half may be used now.</param>
    /// <param name="load">Loads the half and answers the cell, a <see cref="ListCellBase"/>.</param>
    /// <param name="populate">Whether a list reads the field populated for the cell (<c>ListCellAttribute.Populate</c>).</param>
    public void AddLazy(string fieldType, Func<bool> isAvailable, Func<CancellationToken, Task<Type>> load, bool populate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(load);
        _cells.Remove(fieldType);
        _lazy[fieldType] = new LazyFieldComponent(isAvailable, load, typeof(ListCellBase));
        if (populate)
        {
            _populated.Add(fieldType);
        }
        else
        {
            _populated.Remove(fieldType);
        }
    }

    /// <summary>
    /// Whether a list reads a field of the type populated: the half's cell asks for it and is loaded while its plugin is
    /// enabled, so the read-only text shown otherwise keeps the field as it is.
    /// </summary>
    /// <param name="fieldType">The field type.</param>
    /// <returns><see langword="true"/> when the field is read populated.</returns>
    public bool Populates(string fieldType) =>
        fieldType is not null && _populated.Contains(fieldType) && _lazy.TryGetValue(fieldType, out var lazy) && lazy.Loaded(out _);

    /// <summary>
    /// Whether a column can show the field: it is not hidden, and its type has a cell, which an admin half that may be used
    /// provides once loaded, or reads as text.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when a list may offer the field as a column.</returns>
    public bool Shows(SchemaFieldDescription field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return !field.Hidden && (_cells.ContainsKey(field.Type) || (_lazy.TryGetValue(field.Type, out var lazy) && lazy.Available) || TextListCell.Shows(field.Type));
    }

    /// <summary>
    /// The cell of a field type: the one registered for it, the one of an admin half once <c>CellForAsync</c> has loaded it,
    /// or <see cref="TextListCell"/>.
    /// </summary>
    /// <param name="fieldType">The field type.</param>
    /// <returns>The component type of the cell.</returns>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type CellFor(string fieldType)
    {
        if (fieldType is not null && _lazy.TryGetValue(fieldType, out var lazy))
        {
            return lazy.Loaded(out var cell) ? cell : typeof(TextListCell);
        }

        return fieldType is not null && _cells.TryGetValue(fieldType, out var registration) ? registration.Cell : typeof(TextListCell);
    }

    /// <summary>The cell of a field type, as <c>CellFor</c> answers it once the admin half that provides it is loaded.</summary>
    /// <param name="fieldType">The field type.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The component type of the cell.</returns>
    public async ValueTask<Type> CellForAsync(string fieldType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fieldType);
        if (_lazy.TryGetValue(fieldType, out var lazy))
        {
            await lazy.LoadAsync(cancellationToken);
        }

        return CellFor(fieldType);
    }

    private sealed record Registration(
        [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)][param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type Cell);
}
