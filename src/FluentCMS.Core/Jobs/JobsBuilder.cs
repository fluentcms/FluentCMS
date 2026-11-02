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

    // The type is what cms_job stores, so it must outlive renames of the class. Each attempt resolves the job from a
    // scope of its own.
    public JobsBuilder AddJob<TJob, TPayload>(string type)
        where TJob : class, IJob<TPayload>
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(type.Length, JobScheduler.TypeMaxLength, nameof(type));

        _services.AddTransient<TJob>();
        _services.AddSingleton(JobRegistration.Create<TJob, TPayload>(type));
        return this;
    }
}
