namespace FluentCMS.Core.Authorization;

// One permission of the catalogue.
public sealed record PermissionEntry(string Name, string Description, PermissionSource Source);
