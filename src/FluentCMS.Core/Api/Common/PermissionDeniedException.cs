namespace FluentCMS.Core.Api.Common;

// An instance-level permission the caller lacks, for an endpoint that is about no content type or settings area. It
// becomes a 401 for a caller that is not signed in and a 403 for one that is.
internal sealed class PermissionDeniedException(string permission) : Exception($"The permission {permission} is required.")
{
    public string Permission { get; } = permission;
}
