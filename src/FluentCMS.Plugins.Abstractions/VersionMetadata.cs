namespace FluentCMS.Plugins;

// A route that takes If-Match, or one whose success gives the ETag, which core's operation transformer writes into the
// OpenAPI document.
internal sealed record VersionMetadata(bool IfMatch);
