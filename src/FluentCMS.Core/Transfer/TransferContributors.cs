using System.Text.RegularExpressions;
using FluentCMS.Agents;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Plugins;
using FluentCMS.Sites;
using FluentCMS.Transfer;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Transfer;

// The contributors the plugins added, in plugin order, as registered with AddTransferContributor.
internal sealed record TransferContributorRegistrations(IReadOnlyList<McpProviderRegistration> Contributors);

internal sealed record TransferContributor(string Plugin, ITransferContributor Contributor)
{
    public string Name => Contributor.Name;
}

// The contributors of one scope. A name that is not a folder name, or that two contributors share, stops every transfer
// rather than mixing two plugins' files.
internal sealed partial class TransferContributors(TransferContributorRegistrations registrations, IServiceProvider services, IPluginState plugins)
{
    private List<TransferContributor>? _all;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    public IReadOnlyList<TransferContributor> All => _all ??= Resolve();

    public async Task<List<TransferContributor>> EnabledInAsync(SiteId site, CancellationToken cancellationToken)
    {
        List<TransferContributor> enabled = [];
        foreach (var contributor in All)
        {
            if (await plugins.IsEnabledAsync(contributor.Plugin, site, cancellationToken))
            {
                enabled.Add(contributor);
            }
        }

        return enabled;
    }

    private List<TransferContributor> Resolve()
    {
        List<TransferContributor> all = [];
        foreach (var registration in registrations.Contributors)
        {
            var contributor = (ITransferContributor)services.GetRequiredService(registration.Provider);
            var name = contributor.Name;
            if (name is null || !NamePattern().IsMatch(name) || all.Exists(other => other.Name == name))
            {
                throw new FluentCmsConfigurationException(
                    TransferErrorCodes.InvalidContributor,
                    $"The transfer contributor {registration.Provider.FullName} of the plugin '{registration.Plugin}' is named '{name}'; a contributor's name is lower case letters, digits and hyphens, and unique among the contributors of the host.");
            }

            all.Add(new TransferContributor(registration.Plugin, contributor));
        }

        return all;
    }
}
