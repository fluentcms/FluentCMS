using System.Collections.Frozen;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// A name can be registered once: a plugin cannot replace a core type, and two plugins cannot share a name. The rule of a name
// and the duplicates are checked here too, for a type registered as an IFieldType service without AddFieldType.
public sealed class FieldTypeRegistry : IFieldTypeRegistry
{
    private readonly FrozenDictionary<string, IFieldType> _byName;

    public FieldTypeRegistry(IEnumerable<IFieldType> additional)
    {
        ArgumentNullException.ThrowIfNull(additional);

        Dictionary<string, IFieldType> byName = new(StringComparer.Ordinal);
        foreach (var fieldType in CoreFieldTypes().Concat(additional))
        {
            ArgumentNullException.ThrowIfNull(fieldType);
            if (!FieldTypeNames.IsValid(fieldType.Name))
            {
                throw new ArgumentException($"The field type {fieldType.GetType().FullName} is named '{fieldType.Name}', which is not an ASCII letter followed by ASCII letters, digits, '.', '-' or '_'.", nameof(additional));
            }

            if (!byName.TryAdd(fieldType.Name, fieldType))
            {
                throw new ArgumentException($"A field type named '{fieldType.Name}' is registered more than once.", nameof(additional));
            }
        }

        _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);
        All = byName.Values.OrderBy(fieldType => fieldType.Name, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    // The core types only.
    public static FieldTypeRegistry Core { get; } = new([]);

    public IReadOnlyList<IFieldType> All { get; }

    public IFieldType? Find(string name) => name is null ? null : _byName.GetValueOrDefault(name);

    private static IEnumerable<IFieldType> CoreFieldTypes() =>
    [
        new TextFieldType(),
        new LongTextFieldType(),
        new RichTextFieldType(),
        new IntegerFieldType(),
        new DecimalFieldType(),
        new BooleanFieldType(),
        new DateFieldType(),
        new DateTimeFieldType(),
        new EmailFieldType(),
        new UrlFieldType(),
        new SlugFieldType(),
        new SelectFieldType(),
        new JsonFieldType(),
        new RelationFieldType(),
        new ComponentFieldType(),
        new BlocksFieldType(),
    ];
}
