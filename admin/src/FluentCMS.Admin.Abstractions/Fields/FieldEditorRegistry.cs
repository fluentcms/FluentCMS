using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Admin.Fields;

/// <summary>
/// The editors of the field types, by field type, which a generated form looks up for each field. The admin registers the
/// editors of the core field types and an admin half those of its own; a field type without an editor shows its JSON
/// read-only.
/// </summary>
public sealed class FieldEditorRegistry
{
    private readonly ConcurrentDictionary<string, FieldEditorRegistration> _editors = new(StringComparer.Ordinal);

    /// <summary>Registers the editor of a field type, in place of one registered before.</summary>
    /// <typeparam name="TEditor">The editor.</typeparam>
    /// <param name="fieldType">The field type, as the schema names it, such as <c>text</c> or a plugin's <c>media</c>.</param>
    public void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEditor>(string fieldType)
        where TEditor : FieldEditorBase
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        _editors[fieldType] = new FieldEditorRegistration(fieldType, typeof(TEditor));
    }

    /// <summary>Finds the editor of a field type.</summary>
    /// <param name="fieldType">The field type.</param>
    /// <returns>The registration, or <see langword="null"/> when no editor is registered for the type.</returns>
    public FieldEditorRegistration? Find(string fieldType)
    {
        ArgumentNullException.ThrowIfNull(fieldType);
        return _editors.GetValueOrDefault(fieldType);
    }
}

/// <summary>The editor of one field type in a <see cref="FieldEditorRegistry"/>.</summary>
public sealed class FieldEditorRegistration
{
    internal FieldEditorRegistration(string fieldType, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type editor)
    {
        FieldType = fieldType;
        Editor = editor;
    }

    /// <summary>Gets the field type.</summary>
    public string FieldType { get; }

    /// <summary>Gets the editor, a <see cref="FieldEditorBase"/>.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type Editor { get; }
}
