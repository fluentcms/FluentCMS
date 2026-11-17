using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Plugins;

// The names of sections and widget sizes in a manifest. A name this version of the contract does not know reads as a value
// outside the enumeration and not as an error, so a manifest written for a later version does not stop the read of a list
// that holds it; AdminPluginManifest.IsValid tells such a manifest apart.
internal static class ManifestNames
{
    private const string UnknownName = "unknown";

    private const int Unknown = -1;

    private static readonly string[] Sections = ["content", "media", "administration", "settings"];

    private static readonly string[] Sizes = ["small", "medium", "wide"];

    public static string Of(AdminSection section) => Name(Sections, (int)section);

    public static string Of(WidgetSize size) => Name(Sizes, (int)size);

    public static bool IsKnown(AdminSection section) => (int)section >= 0 && (int)section < Sections.Length;

    public static bool IsKnown(WidgetSize size) => (int)size >= 0 && (int)size < Sizes.Length;

    public static AdminSection Section(string? name) => (AdminSection)Index(Sections, name);

    public static WidgetSize Size(string? name) => (WidgetSize)Index(Sizes, name);

    private static string Name(string[] names, int value) => value >= 0 && value < names.Length ? names[value] : UnknownName;

    private static int Index(string[] names, string? name) =>
        name is null ? Unknown : Array.FindIndex(names, known => known.Equals(name, StringComparison.OrdinalIgnoreCase));
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
        return ManifestNames.Section(null);
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
        return ManifestNames.Size(null);
    }

    public override void Write(Utf8JsonWriter writer, WidgetSize value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ManifestNames.Of(value));
}
