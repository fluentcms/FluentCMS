using FluentCMS.Events;

namespace FluentCMS.Plugins.Mcp;

// The activity rows of what a site tells its agents. They carry no text of an instruction or a prompt. Type is null
// for the guidelines of the site.
public sealed record McpInstructionSavedEvent(string? Type) : CmsEvent;

public sealed record McpInstructionDeletedEvent(string? Type) : CmsEvent;

public sealed record McpPromptCreatedEvent(Guid PromptId, string Name) : CmsEvent;

public sealed record McpPromptUpdatedEvent(Guid PromptId, string Name) : CmsEvent;

public sealed record McpPromptDeletedEvent(Guid PromptId, string Name) : CmsEvent;
