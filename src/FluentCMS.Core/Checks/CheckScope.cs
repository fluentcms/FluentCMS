using System.Buffers;
using System.Text.Json;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Checks;

// Which content a check applies to: content types by slug and fields by name, each null for all of them (RUL-03).
public sealed record CheckScope(IReadOnlyList<string>? ContentTypes = null, IReadOnlyList<string>? Fields = null)
{
    public static CheckScope All { get; } = new();

    // Content of no content type, such as a draft whose type is not named, is checked only by checks that apply to every type.
    public bool AppliesTo(string? contentType)
    {
        return ContentTypes is null || (contentType is not null && ContentTypes.Contains(contentType, StringComparer.Ordinal));
    }

    // The subject with only the fields the scope names, in its values and its definition alike, so a type reads every field it is
    // given and a required field the scope leaves out is not missing.
    public CheckSubject Narrow(CheckSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (Fields is null)
        {
            return subject;
        }

        var definition = subject.Definition is { } full
            ? new ContentTypeDefinition(full.Fields.Where(field => Fields.Contains(field.Name, StringComparer.Ordinal)), full.IsEntity, full.Description)
            : null;

        if (subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return subject with { Definition = definition };
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in subject.Fields.EnumerateObject().Where(property => Fields.Contains(property.Name, StringComparer.Ordinal)))
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return subject with { Fields = document.RootElement.Clone(), Definition = definition };
    }
}
