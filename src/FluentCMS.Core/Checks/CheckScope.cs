using System.Buffers;
using System.Text.Json;

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

    // The subject with only the fields the scope names, so a type reads every field it is given.
    public CheckSubject Narrow(CheckSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (Fields is null || subject.Fields.ValueKind != JsonValueKind.Object)
        {
            return subject;
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
        return subject with { Fields = document.RootElement.Clone() };
    }
}
