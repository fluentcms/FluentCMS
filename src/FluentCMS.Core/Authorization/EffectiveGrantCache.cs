using System.Collections.Concurrent;

namespace FluentCMS.Core.Authorization;

// The grants of a role at one permissions_version, across requests and without a shared cache between instances: every
// request reads the stamps, so a change that another instance committed is seen on the next request. The Public role of
// each site is kept the same way, with the stamp it had when it was found.
internal sealed class EffectiveGrantCache
{
    private readonly ConcurrentDictionary<Guid, (long Version, IReadOnlyList<EffectiveGrant> Grants)> _roles = new();
    private readonly ConcurrentDictionary<Guid, (Guid Role, long Version)> _public = new();

    public bool TryGetPublic(Guid site, out (Guid Role, long Version) role) => _public.TryGetValue(site, out role);

    public void SetPublic(Guid site, Guid role, long version) => _public[site] = (role, version);

    public bool TryGet(Guid role, long version, out IReadOnlyList<EffectiveGrant> grants)
    {
        if (_roles.TryGetValue(role, out var entry) && entry.Version == version)
        {
            grants = entry.Grants;
            return true;
        }

        grants = [];
        return false;
    }

    public void Set(Guid role, long version, IReadOnlyList<EffectiveGrant> grants) => _roles[role] = (version, grants);

    // A deleted role is never asked for again, so its entry would stay as long as the process. Another instance keeps its
    // own until it restarts, bounded by the number of roles.
    public void Remove(Guid role) => _roles.TryRemove(role, out _);
}
