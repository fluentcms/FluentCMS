using System.Text.Json;

namespace FluentCMS.Core.Content;

// ContentType is the slug of the entry's content type.
public sealed record NewEntry(string ContentType, JsonElement Fields);
