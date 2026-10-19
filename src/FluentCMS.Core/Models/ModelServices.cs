using Microsoft.Extensions.AI;

namespace FluentCMS.Core.Models;

// The profiles the owner configured; Infrastructure reads them from configuration.
public interface IModelProfiles
{
    IReadOnlyList<ModelProfile> All { get; }

    ModelProfile? Find(string name);
}

// Builds the client of a profile's provider. Only Infrastructure's wiring implements it, and only the gateway calls it, so no other
// code holds a provider client (ADR-0008, MDL-01).
public interface IModelClients
{
    IChatClient Create(ModelProfile profile);
}

// The hosts the administrator allows outbound requests to (SEC-05): model endpoints now, and the links checks follow later.
public interface IOutboundHosts
{
    bool IsAllowed(string host);
}
