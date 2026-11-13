using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Cli.Generate;

// A schema snapshot (spec 4.2) as the model the emitters read, with its hash. The text is the input, whether it came from
// a file, an instance or a host, so the three give the same files. A field type that core does not know, such as the media
// plugin's, is read as the schema tools read it, through UncheckedFieldTypes; the tool has no plugins. Such a field type is
// then text of the snapshot rather than of code, and the emitters write it into comments, so Read refuses one that breaks
// the rule of IFieldType.Name, which core holds every field type to (FieldTypeNames.IsValid).
internal sealed record ClientSchema(SchemaModel Model, string Hash)
{
    public static ClientSchema Read(string snapshot, string source)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SchemaModel model;
        try
        {
            model = new SchemaBuilder(new UncheckedFieldTypes()).AddSchemaJson(snapshot).Build();
        }
        catch (Exception exception) when (exception is JsonException or SchemaValidationException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new CliException(CliErrorCodes.SchemaUnreadable, $"{source} is not a schema snapshot: {exception.Message}");
        }

        // The type and field names are checked by now, so the message can name them.
        var fields = model.ContentTypes.Concat(model.Components).Concat(model.Blocks)
            .SelectMany(type => type.Fields.Where(field => !FieldTypeNames.IsValid(field.FieldType)).Select(field => $"'{type.Name}.{field.Name}'"))
            .ToList();
        if (fields.Count > 0)
        {
            throw new CliException(
                CliErrorCodes.SchemaUnreadable,
                $"{source} is not a schema snapshot: {(fields.Count == 1 ? "the field" : "the fields")} {string.Join(", ", fields)} {(fields.Count == 1 ? "has" : "have")} a field type that is not a name; a field type starts with a letter and holds only letters, digits, '.', '-' and '_'.");
        }

        return new ClientSchema(model, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))));
    }
}
