using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Sso;

// 90xx, the event ids of the identity family (FCMS30xx); admin single sign-on has 9040 to 9049, and linking external
// logins to users 9060 to 9069.
internal static partial class SsoLog
{
    public const int UserProvisionedId = 9040;
    public const int RemoteFailureId = 9041;
    public const int RoleNotFoundId = 9042;
    public const int ProvisioningRefusedId = 9043;
    public const int NoGroupsId = 9044;
    public const int UserLinkedId = 9060;
    public const int InvitationAcceptedId = 9061;
    public const int LinkRefusedId = 9062;

    [LoggerMessage(EventId = UserProvisionedId, EventName = "SsoUserProvisioned", Level = LogLevel.Information, Message = "Created user {UserId} at the first sign-in through the SSO provider {Provider}.")]
    public static partial void UserProvisioned(ILogger logger, Guid userId, string provider);

    [LoggerMessage(EventId = RemoteFailureId, EventName = "SsoRemoteFailure", Level = LogLevel.Warning, Message = "The sign-in through the SSO provider {Provider} did not complete: {Failure}")]
    public static partial void RemoteFailure(ILogger logger, string provider, string failure);

    [LoggerMessage(EventId = RoleNotFoundId, EventName = "SsoRoleNotFound", Level = LogLevel.Warning, Message = "{Code}: the SSO provider {Provider} names the role '{Role}', which does not exist; it is not given.")]
    public static partial void RoleNotFound(ILogger logger, string code, string provider, string role);

    [LoggerMessage(EventId = ProvisioningRefusedId, EventName = "SsoProvisioningRefused", Level = LogLevel.Warning, Message = "{Code}: no user was created at the sign-in through the SSO provider {Provider}: {Reason}")]
    public static partial void ProvisioningRefused(ILogger logger, string code, string provider, string reason);

    [LoggerMessage(EventId = NoGroupsId, EventName = "SsoNoGroups", Level = LogLevel.Warning, Message = "The token of the SSO provider {Provider} has no '{GroupsClaim}' claim, so the user holds none of the roles the provider maps.")]
    public static partial void NoGroups(ILogger logger, string provider, string groupsClaim);

    [LoggerMessage(EventId = UserLinkedId, EventName = "SsoUserLinked", Level = LogLevel.Information, Message = "Linked user {UserId}, found by its email address, to the SSO provider {Provider} at its first sign-in through it.")]
    public static partial void UserLinked(ILogger logger, Guid userId, string provider);

    [LoggerMessage(EventId = InvitationAcceptedId, EventName = "SsoInvitationAccepted", Level = LogLevel.Information, Message = "Created user {UserId} from an invitation accepted through the SSO provider {Provider}.")]
    public static partial void InvitationAccepted(ILogger logger, Guid userId, string provider);

    [LoggerMessage(EventId = LinkRefusedId, EventName = "SsoLinkRefused", Level = LogLevel.Warning, Message = "{Code}: the sign-in through the SSO provider {Provider} was not linked to a user: {Reason}")]
    public static partial void LinkRefused(ILogger logger, string code, string provider, string reason);
}
