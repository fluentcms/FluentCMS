using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin.Plugins;

// The registrations of the admin's container, which a half's provider falls back to: the container is built before any half
// loads, so a half cannot add to it (spec 5.1).
internal sealed class AdminServiceDescriptors(IServiceCollection services)
{
    // Every service of the admin that the half did not register itself resolves from the admin's provider, so the half shares
    // the admin's instances. An open generic, such as a logger or a localizer of a type, is registered as the admin registers
    // it, over the services forwarded so. The half's provider is never disposed, so it never disposes what it forwards.
    public void FallBackTo(IServiceCollection half, IServiceProvider admin)
    {
        ArgumentNullException.ThrowIfNull(half);
        ArgumentNullException.ThrowIfNull(admin);
        var own = half.Select(descriptor => descriptor.ServiceType).ToHashSet();
        foreach (var registrations in services.Where(descriptor => !descriptor.IsKeyedService && !own.Contains(descriptor.ServiceType)).GroupBy(descriptor => descriptor.ServiceType).ToList())
        {
            var type = registrations.Key;
            if (type.IsGenericTypeDefinition)
            {
                foreach (var descriptor in registrations)
                {
                    half.Add(descriptor);
                }
            }
            else if (registrations.Count() == 1)
            {
                half.Add(ServiceDescriptor.Transient(type, _ => admin.GetRequiredService(type)));
            }
            else
            {
                // Each of several registrations, such as the configurations of an options type, so that IEnumerable<T> in
                // the half holds what it holds in the admin.
                for (var index = 0; index < registrations.Count(); index++)
                {
                    var at = index;
                    half.Add(ServiceDescriptor.Transient(type, _ => admin.GetServices(type).ElementAt(at)!));
                }
            }
        }
    }
}
