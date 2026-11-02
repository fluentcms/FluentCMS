using FluentCMS.Content;

namespace FluentCMS.Testing;

public sealed record TestUser(Guid Id, string Name, string Email, string Role, Guid RoleId, Principal Principal);
