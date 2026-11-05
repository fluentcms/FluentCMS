using FluentCMS.Core.Api.Management;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluentCMS.Core.Health;

public static class HealthRegistration
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    // A check of a plugin or a storage provider that the instance needs to serve traffic, in one call. The failure of a
    // ready check takes the instance out of rotation, which is why a check of something the instance can do without
    // should report Degraded instead.
    public static IHealthChecksBuilder AddFluentCmsReadyCheck<T>(this IHealthChecksBuilder builder, string name, HealthStatus? failureStatus = null)
        where T : class, IHealthCheck
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return builder.AddCheck<T>(name, failureStatus, [FluentCmsHealthCheckTags.Ready], CheckTimeout);
    }

    internal static IServiceCollection AddFluentCmsHealth(this IServiceCollection services)
    {
        services.AddFluentCmsOptions(HealthOptions.Section, HostErrorCodes.InvalidOption, new HealthOptions(), health => health.Validate());
        services.AddSingleton<JobRunnerPulse>();

        services.AddHealthChecks()
            .AddCheck("process", () => HealthCheckResult.Healthy("The process answers."), [FluentCmsHealthCheckTags.Live])
            .AddFluentCmsReadyCheck<DatabaseHealthCheck>("database")
            .AddFluentCmsReadyCheck<JobRunnerHealthCheck>("job-runner");

        services.AddFluentCmsEndpoints(FluentCmsEndpointGroup.Health, MapProbes);
        services.AddSystemHealthApi();
        return services;
    }

    // The public probes answer with the status alone: the default writer prints Healthy, Degraded or Unhealthy and
    // nothing of an exception or a description. Unhealthy is a 503, so an orchestrator needs no body.
    private static void MapProbes(RouteGroupBuilder health)
    {
        health.MapHealthChecks("/live", Probe(FluentCmsHealthCheckTags.Live));
        health.MapHealthChecks("/ready", Probe(FluentCmsHealthCheckTags.Ready));
    }

    private static HealthCheckOptions Probe(string tag) => new()
    {
        Predicate = registration => registration.Tags.Contains(tag),
        AllowCachingResponses = false,
    };
}
