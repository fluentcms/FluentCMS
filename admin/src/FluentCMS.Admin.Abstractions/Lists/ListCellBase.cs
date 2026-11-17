using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Lists;

/// <summary>
/// The base class of a list cell: what a generated list shows of one field of an entry in its row. A cell is registered for a
/// field type with <see cref="ListCellRegistry"/>. It shows the value only, and holds nothing that takes focus, since a row
/// of the list is one tab stop.
/// </summary>
public abstract class ListCellBase : ComponentBase
{
    /// <summary>Gets or sets the field the cell shows.</summary>
    [Parameter, EditorRequired]
    public SchemaFieldDescription Field { get; set; } = null!;

    /// <summary>Gets or sets the entry of the row, as the list read it.</summary>
    [Parameter, EditorRequired]
    public ContentEntry Entry { get; set; } = null!;

    /// <summary>Gets the value of <see cref="Field"/> in <see cref="Entry"/>, or <see langword="null"/> when it has none.</summary>
    protected JsonNode? Value => Entry?.Fields[Field.Name];
}
