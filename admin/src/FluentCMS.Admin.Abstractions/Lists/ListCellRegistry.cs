using System.Diagnostics.CodeAnalysis;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Lists;

/// <summary>
/// The list cells by field type, which the generated lists draw their columns with. A field type without a cell of its own
/// is shown by <see cref="TextListCell"/> when its value reads as text, and is offered as no column otherwise.
/// </summary>
public sealed class ListCellRegistry
{
    private readonly Dictionary<string, Registration> _cells = new(StringComparer.Ordinal);

    /// <summary>Registers the cell of a field type, in place of the one before.</summary>
    /// <typeparam name="TCell">The cell component.</typeparam>
    /// <param name="fieldType">The field type, as <see cref="SchemaFieldDescription.Type"/> names it.</param>
    public void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCell>(string fieldType)
        where TCell : ListCellBase
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        _cells[fieldType] = new Registration(typeof(TCell));
    }

    /// <summary>Whether a column can show the field: it is not hidden, and its type has a cell or reads as text.</summary>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when a list may offer the field as a column.</returns>
    public bool Shows(SchemaFieldDescription field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return !field.Hidden && (_cells.ContainsKey(field.Type) || TextListCell.Shows(field.Type));
    }

    /// <summary>The cell of a field type: the one registered for it, or <see cref="TextListCell"/>.</summary>
    /// <param name="fieldType">The field type.</param>
    /// <returns>The component type of the cell.</returns>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type CellFor(string fieldType) =>
        fieldType is not null && _cells.TryGetValue(fieldType, out var registration) ? registration.Cell : typeof(TextListCell);

    private sealed record Registration([property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type Cell);
}
