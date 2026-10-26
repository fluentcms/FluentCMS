using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Media;

internal static class MediaFields
{
    // A field that holds an asset of the media library, or a list of them.
    public static bool Holds(FieldDefinition field)
    {
        return field.Type == "media"
            || (field.Type == "list" && field.Settings.TryGetProperty("itemType", out var itemType) && itemType.GetString() == "media");
    }
}
