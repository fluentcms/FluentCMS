using System.Collections.Frozen;
using System.Text.Json;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Jobs;

// How to run one job type, without reflection at run time.
internal sealed class JobRegistration
{
    private JobRegistration(string type, Type job, Type payload, Func<string, object?> read, Func<IServiceProvider, object, JobContext, Task> invoke)
    {
        Type = type;
        Job = job;
        Payload = payload;
        Read = read;
        Invoke = invoke;
    }

    public string Type { get; }

    public Type Job { get; }

    public Type Payload { get; }

    public Func<string, object?> Read { get; }

    public Func<IServiceProvider, object, JobContext, Task> Invoke { get; }

    public static JobRegistration Create<TJob, TPayload>(string type)
        where TJob : class, IJob<TPayload> =>
        new(
            type,
            typeof(TJob),
            typeof(TPayload),
            json => JsonSerializer.Deserialize<TPayload>(json, JobJson.Options),
            (services, payload, context) => services.GetRequiredService<TJob>().ExecuteAsync((TPayload)payload, context));
}

internal static class JobJson
{
    public static readonly JsonSerializerOptions Options = JsonSerializerOptions.Web;
}

internal sealed class JobRegistry
{
    private readonly FrozenDictionary<string, JobRegistration> _byType;
    private readonly FrozenDictionary<(Type Job, Type Payload), string> _byJob;

    public JobRegistry(IEnumerable<JobRegistration> registrations)
    {
        var all = registrations.ToList();
        var duplicate = all.GroupBy(registration => registration.Type, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new FluentCmsConfigurationException(JobErrorCodes.DuplicateJobType, $"The job type '{duplicate.Key}' is registered {duplicate.Count()} times.");
        }

        _byType = all.ToFrozenDictionary(registration => registration.Type, StringComparer.Ordinal);
        _byJob = all
            .GroupBy(registration => (registration.Job, registration.Payload))
            .ToFrozenDictionary(group => group.Key, group => group.First().Type);
    }

    public JobRegistration? Find(string type) => _byType.GetValueOrDefault(type);

    public string TypeOf<TJob, TPayload>() =>
        _byJob.TryGetValue((typeof(TJob), typeof(TPayload)), out var type)
            ? type
            : throw new FluentCmsConfigurationException(
                JobErrorCodes.UnregisteredJob,
                $"{typeof(TJob).FullName} is not registered for the payload {typeof(TPayload).FullName}; call cms.Jobs.AddJob inside AddFluentCms.");
}
