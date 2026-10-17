namespace FluentCMS.Core.Identity;

public sealed record UserSummary(Guid Id, string Email, string DisplayName, bool IsDisabled, IReadOnlyList<string> Roles);
