using FluentCMS.Events;

namespace FluentCMS.Core.Authorization;

// Raised in the SaveChanges that raises a role's PermissionsVersion, for core's outbox handlers. It is not in the SDK, so
// no plugin or webhook subscribes to it. Sites are those whose callers may hold the role: its own, or every site for a
// role of the instance.
internal sealed record RolePermissionsChangedEvent(Guid RoleId, IReadOnlyList<Guid> Sites) : CmsEvent;
