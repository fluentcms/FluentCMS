using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Diagnostics;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Hosting;

// A client that the options list under the admin's id wins whole over the one AddAdmin would register, so one that the served
// admin cannot sign in with lets the host start and fails in the browser. While the admin is on, the startup report names
// what such a client has or lacks, and the host still starts (#3178). The registration's own client has and lacks nothing.
internal sealed class AdminClientReport(AdminOptions admin, IOptions<ClientOptions> clients, AdminClientRegistration registration) : IInsecureDefaultSource
{
    public const string Secret = "admin-client-secret";
    public const string Scopes = "admin-client-scopes";
    public const string RedirectUri = "admin-client-redirect-uri";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        if (!admin.Enabled)
        {
            return [];
        }

        ClientOptions options;
        List<string> callbacks;
        try
        {
            options = clients.Value;
            callbacks = registration.CallbackUris();
        }
        catch (FluentCmsConfigurationException)
        {
            // The same refusal stops the host at start, which names it; a report made without a start, as by doctor, goes on.
            return [];
        }

        // With no address the host is refused its start, which names the public origin.
        if (callbacks.Count == 0 || !options.Applications.TryGetValue(options.AdminClientId, out var client))
        {
            return [];
        }

        var listed = $"The admin's client {options.AdminClientId}, which FluentCms:Clients:Applications or cms.Clients.Applications lists,";
        var off = $"or turn the admin off with {AdminOptions.Root}:{AdminOptions.Section}:{nameof(AdminOptions.Enabled)} set to false when this host serves no admin";
        List<InsecureDefault> found = [];
        if (!string.IsNullOrEmpty(client.Secret))
        {
            found.Add(new InsecureDefault
            {
                Id = Secret,
                Message = $"{listed} has a {nameof(ClientRegistration.Secret)}, so it is a confidential client, and the admin at {AdminSite.Path}, which runs in the browser and holds no secret, cannot sign in with it. Remove the {nameof(ClientRegistration.Secret)}, {off}.",
            });
        }

        var missing = AdminSite.Scopes.Where(scope => !client.Scopes.Contains(scope, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            found.Add(new InsecureDefault
            {
                Id = Scopes,
                Message = $"{listed} lacks the scopes {string.Join(", ", missing)}, which the admin at {AdminSite.Path} asks for at sign-in, so the authorization server refuses its sign-in. Add them to its {nameof(ClientRegistration.Scopes)}, {off}.",
            });
        }

        if (!callbacks.Any(callback => client.RedirectUris.Contains(callback, StringComparer.Ordinal)))
        {
            // The report keeps only the scheme and host of a URL, so the path is named apart from it.
            var paths = string.Join(" and ", callbacks.Select(callback => new Uri(callback).AbsolutePath).Distinct(StringComparer.Ordinal));
            found.Add(new InsecureDefault
            {
                Id = RedirectUri,
                Message = $"{listed} lists no redirect URI of the admin this host serves at {AdminSite.Path}, the path {paths} on {AdminOptions.Root}:{PublicOrigin.Key}, or in Development without it on an address the host listens on, so the authorization server refuses its sign-in. Add it to its {nameof(ClientRegistration.RedirectUris)}, {off}.",
            });
        }

        return found;
    }
}
