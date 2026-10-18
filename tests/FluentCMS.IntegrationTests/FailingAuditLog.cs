using FluentCMS.Core.Audit;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests;

// Wraps the registered audit log and throws on an armed append, to stand for a failure partway through a unit of work.
internal sealed class FailingAuditLog(IAuditLog inner, AuditFailure failure) : IAuditLog
{
    public Task<AuditEvent> AppendAsync(NewAuditEvent entry, CancellationToken cancellationToken = default)
    {
        return failure.Trips()
            ? throw new InvalidOperationException($"Injected failure while appending {entry.Operation}.")
            : inner.AppendAsync(entry, cancellationToken);
    }

    public static void Wrap(IServiceCollection services, AuditFailure failure)
    {
        var registered = services.Single(descriptor => descriptor.ServiceType == typeof(IAuditLog));
        services.Remove(registered);
        services.AddScoped<IAuditLog>(provider => new FailingAuditLog(
            (IAuditLog)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!),
            failure));
    }
}

internal sealed class AuditFailure
{
    private int _remaining;

    // The given append from now on throws, counting from one; every other append goes through.
    public void FailOnAppend(int append)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(append, 1);
        _remaining = append;
    }

    public bool Trips()
    {
        return _remaining > 0 && --_remaining == 0;
    }
}
