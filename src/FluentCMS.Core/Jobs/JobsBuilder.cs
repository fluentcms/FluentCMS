using FluentCMS.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Jobs;

public sealed class JobsBuilder
{
    private readonly IServiceCollection _services;

    internal JobsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public JobRunnerOptions Options { get; } = new();

    // The type is what cms_job stores, so it must outlive renames of the class. An attempt timeout of null takes
    // JobRunnerOptions.AttemptTimeout; Timeout.InfiniteTimeSpan puts no limit on this job whatever that is.
    public JobsBuilder AddJob<TJob, TPayload>(string type, TimeSpan? attemptTimeout = null)
        where TJob : class, IJob<TPayload>
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(type.Length, JobScheduler.TypeMaxLength, nameof(type));
        if (attemptTimeout is { } timeout && !JobRunnerOptions.IsValidAttemptTimeout(timeout))
        {
            throw new ArgumentOutOfRangeException(nameof(attemptTimeout), timeout, $"The attempt timeout must be positive and no longer than {JobRunnerOptions.LongestWait.TotalDays} days, or Timeout.InfiniteTimeSpan.");
        }

        _services.AddTransient<TJob>();
        _services.AddSingleton(JobRegistration.Create<TJob, TPayload>(type, attemptTimeout));
        return this;
    }
}
