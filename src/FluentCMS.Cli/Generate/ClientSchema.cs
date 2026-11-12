using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;

namespace FluentCMS.Cli.Generate;

// A schema snapshot (spec 4.2) as the model the emitters read, with its hash. The text is the input, whether it came from
// a file, an instance or a host, so the three give the same files. A field type that core does not know, such as the media
// plugin's, is read as the schema tools read it, through UncheckedFieldTypes; the tool has no plugins.
internal sealed record ClientSchema(SchemaModel Model, string Hash)
{
    public static ClientSchema Read(string snapshot, string source)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var model = new SchemaBuilder(new UncheckedFieldTypes()).AddSchemaJson(snapshot).Build();
            return new ClientSchema(model, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))));
        }
        catch (Exception exception) when (exception is JsonException or SchemaValidationException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new CliException(CliErrorCodes.SchemaUnreadable, $"{source} is not a schema snapshot: {exception.Message}");
        }
    }
}
