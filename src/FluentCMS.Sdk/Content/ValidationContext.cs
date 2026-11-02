using FluentCMS.Schema;

namespace FluentCMS.Content;

/// <summary>What a custom validator knows about the entry being validated, and where it reports problems.</summary>
public sealed class ValidationContext
{
    private readonly List<ValidationError> _errors;

    /// <summary>Creates a context, for the pipeline or for unit tests of a validator.</summary>
    /// <param name="type">The type of the entry.</param>
    /// <param name="values">The values of the entry, by API field name.</param>
    /// <param name="operation">The operation that causes the validation.</param>
    /// <param name="mode">Whether a draft is saved or the entry is published.</param>
    /// <param name="entry">The typed entry, if there is one.</param>
    /// <param name="field">The field being validated, or <see langword="null"/> for an entry validator.</param>
    /// <param name="path">The path of that field; defaults to its name.</param>
    public ValidationContext(
        SchemaType type,
        IReadOnlyDictionary<string, object?> values,
        OperationContext operation,
        ValidationMode mode,
        object? entry = null,
        SchemaField? field = null,
        string? path = null)
        : this(type, values, operation, mode, entry, field, path, [])
    {
    }

    private ValidationContext(
        SchemaType type,
        IReadOnlyDictionary<string, object?> values,
        OperationContext operation,
        ValidationMode mode,
        object? entry,
        SchemaField? field,
        string? path,
        List<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(operation);

        Type = type;
        Values = values;
        Operation = operation;
        Mode = mode;
        Entry = entry;
        Field = field;
        Path = field is null ? "" : path ?? field.Name;
        _errors = errors;
    }

    /// <summary>The type of the entry.</summary>
    public SchemaType Type { get; }

    /// <summary>The field being validated, or <see langword="null"/> for an entry validator.</summary>
    public SchemaField? Field { get; }

    /// <summary>The path of <see cref="Field"/> in the API shape, such as <c>seo.metaTitle</c>; empty for an entry validator.</summary>
    public string Path { get; }

    /// <summary>The typed entry, or <see langword="null"/> when the entry is only a set of values.</summary>
    public object? Entry { get; }

    /// <summary>The values next to the field, by API field name. For a field of a component or block, those of the same component or block.</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }

    /// <summary>The operation that causes the validation; it gives the principal, origin, site and locale.</summary>
    public OperationContext Operation { get; }

    /// <summary>Whether a draft is saved or the entry is published.</summary>
    public ValidationMode Mode { get; }

    /// <summary>Every error added so far, by the declarative rules and by validators.</summary>
    public IReadOnlyList<ValidationError> Errors => _errors;

    /// <summary>Adds an error at a path.</summary>
    /// <param name="path">The path in the API shape; empty for the entry as a whole.</param>
    /// <param name="code">A stable code; plugins should use their own prefix. An empty code becomes <see cref="ValidationErrorCodes.Custom"/>.</param>
    /// <param name="message">A message for people.</param>
    public void AddError(string path, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrEmpty(message);
        _errors.Add(new ValidationError(path, string.IsNullOrEmpty(code) ? ValidationErrorCodes.Custom : code, message));
    }

    /// <summary>Adds an error to the field being validated.</summary>
    /// <param name="code">A stable code. An empty code becomes <see cref="ValidationErrorCodes.Custom"/>.</param>
    /// <param name="message">A message for people.</param>
    /// <exception cref="InvalidOperationException">There is no current field, as in an entry validator.</exception>
    public void AddError(string code, string message)
    {
        if (Field is null)
        {
            throw new InvalidOperationException("There is no current field; give the path of the error.");
        }

        AddError(Path, code, message);
    }

    // Shares the error list, so what a field validator reports lands in the result of the run.
    internal ValidationContext ForField(SchemaField field, string path, IReadOnlyDictionary<string, object?> values) =>
        new(Type, values, Operation, Mode, Entry, field, path, _errors);

    // The entry-level view of the same run.
    internal ValidationContext ForEntry() =>
        new(Type, Values, Operation, Mode, Entry, null, null, _errors);
}
