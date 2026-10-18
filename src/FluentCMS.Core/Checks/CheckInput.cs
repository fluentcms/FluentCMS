using System.Text.Json;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Checks;

// The services are the caller's own scope, so a type that needs more than the content, such as a model, reads it as the caller.
public sealed record CheckInput(JsonElement Settings, CheckSubject Subject, IServiceProvider Services);

// One piece of content as a check sees it: its fields as an entry stores them, the sources its change cites, and its content type
// when it has one. The fields hold only the ones the check's scope names.
public sealed record CheckSubject(
    JsonElement Fields,
    IReadOnlyList<string> Sources,
    string? ContentType = null,
    ContentTypeDefinition? Definition = null);
