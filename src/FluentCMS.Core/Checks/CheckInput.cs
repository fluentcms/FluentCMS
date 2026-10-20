using System.Text.Json;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Checks;

// The services are the caller's own scope, so a type that needs more than the content, such as a model, reads it as the caller.
// OnBehalfOf names whose work the caller judges when it is an engine, such as the agent or person whose change set it checks, so a
// model call counts against that agent's spend cap (SEC-04).
public sealed record CheckInput(JsonElement Settings, CheckSubject Subject, IServiceProvider Services, Initiator? OnBehalfOf = null);

// One piece of content as a check sees it: its fields as an entry stores them, the sources its change cites, and its content type
// when it has one. The fields hold only the ones the check's scope names. ChangedFields names the fields the change touches, and is
// null when every field counts as changed, as for new content or a draft. ChangeSetId names the set the content goes live with,
// so a check can read what that set proposes for the items the content refers to.
public sealed record CheckSubject(
    JsonElement Fields,
    IReadOnlyList<string> Sources,
    string? ContentType = null,
    ContentTypeDefinition? Definition = null,
    IReadOnlyList<string>? ChangedFields = null,
    Guid? ChangeSetId = null);
