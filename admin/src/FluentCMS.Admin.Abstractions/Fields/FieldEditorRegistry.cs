using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using FluentCMS.Admin.Plugins;

namespace FluentCMS.Admin.Fields;

/// <summary>
/// The editors of the field types, by field type, which a generated form looks up for each field. The admin registers the
/// editors of the core field types, and the editors of an admin half that is not loaded yet are added with <c>AddLazy</c>
/// and found with <c>FindAsync</c>, which loads the half; a field type without an editor shows its JSON read-only.
/// </summary>
public sealed class FieldEditorRegistry
{
    private readonly ConcurrentDictionary<string, FieldEditorRegistration> _editors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LazyFieldComponent> _lazy = new(StringComparer.Ordinal);

    /// <summary>Registers the editor of a field type, in place of one registered before.</summary>
    /// <typeparam name="TEditor">The editor.</typeparam>
    /// <param name="fieldType">The field type, as the schema names it, such as <c>text</c> or a plugin's <c>media</c>.</param>
    public void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEditor>(string fieldType)
        where TEditor : FieldEditorBase
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        _lazy.TryRemove(fieldType, out _);
        _editors[fieldType] = new FieldEditorRegistration(fieldType, typeof(TEditor));
    }

    /// <summary>
    /// Registers the editor of a field type that an admin half provides, in place of one registered before. While
    /// <paramref name="isAvailable"/> answers <see langword="false"/>, as when the half's plugin is disabled in the site,
    /// the type has no editor.
    /// </summary>
    /// <param name="fieldType">The field type, as the schema names it.</param>
    /// <param name="isAvailable">Whether the half may be used now.</param>
    /// <param name="load">Loads the half and answers the editor, a <see cref="FieldEditorBase"/>.</param>
    public void AddLazy(string fieldType, Func<bool> isAvailable, Func<CancellationToken, Task<Type>> load)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(load);
        _editors.TryRemove(fieldType, out _);
        _lazy[fieldType] = new LazyFieldComponent(isAvailable, load, typeof(FieldEditorBase));
    }

    /// <summary>Finds the editor of a field type, of an admin half only once <c>FindAsync</c> has loaded it.</summary>
    /// <param name="fieldType">The field type.</param>
    /// <returns>The registration, or <see langword="null"/> when no editor is registered for the type.</returns>
    public FieldEditorRegistration? Find(string fieldType)
    {
        ArgumentNullException.ThrowIfNull(fieldType);
        if (_lazy.TryGetValue(fieldType, out var lazy))
        {
            return lazy.Loaded(out var editor) ? new FieldEditorRegistration(fieldType, editor) : null;
        }

        return _editors.GetValueOrDefault(fieldType);
    }

    /// <summary>Finds the editor of a field type, and loads the admin half that provides it first when it is not loaded.</summary>
    /// <param name="fieldType">The field type.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The registration, or <see langword="null"/> when no editor is registered for the type or its half may not be used.</returns>
    public async ValueTask<FieldEditorRegistration?> FindAsync(string fieldType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fieldType);
        if (_lazy.TryGetValue(fieldType, out var lazy))
        {
            return await lazy.LoadAsync(cancellationToken) && lazy.Loaded(out var editor) ? new FieldEditorRegistration(fieldType, editor) : null;
        }

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
