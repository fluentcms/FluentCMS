using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Plugins;

// The names of sections and widget sizes in a manifest. A name this version of the contract does not know reads as the
// first value, so a manifest written for a later version, or a list that holds one, still reads.
internal static class ManifestNames
{
    private static readonly string[] Sections = ["content", "media", "administration", "settings"];

    private static readonly string[] Sizes = ["small", "medium", "wide"];

    public static string Of(AdminSection section) => Name(Sections, (int)section);

    public static string Of(WidgetSize size) => Name(Sizes, (int)size);

    public static bool IsSection(string? name) => Index(Sections, name) >= 0;

    public static bool IsSize(string? name) => Index(Sizes, name) >= 0;

    public static AdminSection Section(string? name) => (AdminSection)Math.Max(0, Index(Sections, name));

    public static WidgetSize Size(string? name) => (WidgetSize)Math.Max(0, Index(Sizes, name));

    private static string Name(string[] names, int value) => value >= 0 && value < names.Length ? names[value] : names[0];

    private static int Index(string[] names, string? name) =>
        name is null ? -1 : Array.FindIndex(names, known => known.Equals(name, StringComparison.OrdinalIgnoreCase));
}

internal sealed class AdminSectionJsonConverter : JsonConverter<AdminSection>
{
    public override AdminSection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ManifestNames.Section(reader.GetString());
        }

        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, AdminSection value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ManifestNames.Of(value));
}

internal sealed class WidgetSizeJsonConverter : JsonConverter<WidgetSize>
{
    public override WidgetSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ManifestNames.Size(reader.GetString());
        }

        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, WidgetSize value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ManifestNames.Of(value));
}
