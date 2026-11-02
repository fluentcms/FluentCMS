using FluentCMS.Content;

namespace FluentCMS.Testing;

// Secret is the key as a client sends it, in the format of spec 4.7.
public sealed record TestApiKey(Guid Id, string Name, ApiKeyClass Class, string Role, Guid RoleId, string Secret, Principal Principal);
