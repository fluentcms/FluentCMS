using System.Text.Json;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Checks;

// The services are the caller's own scope, so a type that needs more than the content, such as a model, reads it as the caller.
// ChargedTo names the agent a model call counts against when the caller is an engine judging that agent's change set (SEC-04).
public sealed record CheckInput(JsonElement Settings, CheckSubject Subject, IServiceProvider Services, Guid? ChargedTo = null);

// One piece of content as a check sees it: its fields as an entry stores them, the sources its change cites, and its content type
// when it has one. The fields hold only the ones the check's scope names. ChangedFields names the fields the change touches, and is
// null when every field counts as changed, as for new content or a draft.
public sealed record CheckSubject(
    JsonElement Fields,
    IReadOnlyList<string> Sources,
    string? ContentType = null,
    ContentTypeDefinition? Definition = null,
    IReadOnlyList<string>? ChangedFields = null);
