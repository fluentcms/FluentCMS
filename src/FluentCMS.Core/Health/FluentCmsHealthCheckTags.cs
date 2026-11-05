namespace FluentCMS.Core.Health;

// The tags that group health checks: /health/live runs the checks tagged Live, /health/ready those tagged Ready.
public static class FluentCmsHealthCheckTags
{
    // The process answers. A live check must not depend on anything outside the process, or an outage of the database
    // would have the orchestrator restart every instance.
    public const string Live = "live";

    // The instance can serve traffic: the database, the job runner, and what plugins and storage providers add.
    public const string Ready = "ready";
}
