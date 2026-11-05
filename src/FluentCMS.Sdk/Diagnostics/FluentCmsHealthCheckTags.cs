namespace FluentCMS.Diagnostics;

/// <summary>The tags that group health checks. <c>/health/live</c> runs the checks tagged <see cref="Live"/> and <c>/health/ready</c> those tagged <see cref="Ready"/>; a plugin tags its own checks with them.</summary>
public static class FluentCmsHealthCheckTags
{
    /// <summary>The process answers. A live check must not depend on anything outside the process, or an outage of the database would have the orchestrator restart every instance.</summary>
    public const string Live = "live";

    /// <summary>The instance can serve traffic: the database, the job runner, and what plugins and storage providers add. A failing ready check takes the instance out of rotation.</summary>
    public const string Ready = "ready";
}
