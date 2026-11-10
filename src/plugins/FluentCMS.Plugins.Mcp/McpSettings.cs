using System.ComponentModel.DataAnnotations;
using FluentCMS.Agents;
using FluentCMS.Diagnostics;
using FluentCMS.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Plugins.Mcp;

// The settings area "mcp" (settings.mcp.manage). How MCP clients register themselves at the authorization server is a
// matter of the instance, which has one authorization server, so the instance defaults are what is read; what a site
// sets for itself here changes nothing of it.
[Settings("mcp")]
public sealed class McpSettings
{
    // Approval: a registration waits for an administrator. Open: it is approved at once with DefaultClientRole. Off: no
    // client registers itself.
    public McpClientRegistrationMode ClientRegistration { get; set; } = McpClientRegistrationMode.Approval;

    // The role a client gets under Open: the id of a role, or the exact name of an instance-wide role. While it is unset
    // or names no role, a registration waits for approval as under Approval.
    [StringLength(128)]
    public string? DefaultClientRole { get; set; }

    // How many clients may wait for approval at once.
    [Range(1, 10000)]
    public int MaxPendingClients { get; set; } = 100;

    // How many clients one client address may register in an hour, on each instance.
    [Range(1, 1000)]
    public int RegistrationsPerHour { get; set; } = 10;
}

// The policy core asks on a registration and for the discovery document: the instance defaults of the settings.
internal sealed class McpClientRegistrationPolicySource(ISettings<McpSettings> settings) : IMcpClientRegistrationPolicy
{
    public async ValueTask<McpClientRegistrationPolicy> GetAsync(CancellationToken cancellationToken = default)
    {
        var current = await settings.GetDefaultAsync(cancellationToken);
        return new McpClientRegistrationPolicy(current.ClientRegistration)
        {
            DefaultRole = current.DefaultClientRole,
            MaxPendingClients = current.MaxPendingClients,
            RegistrationsPerHour = current.RegistrationsPerHour,
        };
    }
}

// Open registration lets anyone on the network make a client that users can sign in through at once, so the startup
// report lists it in Production. The report is made once per start, so a change of the setting shows at the next one.
internal sealed class McpInsecureDefaults(IServiceScopeFactory scopes, IHostEnvironment environment) : IInsecureDefaultSource
{
    public const string OpenClientRegistration = "mcp-open-client-registration";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        if (!environment.IsProduction())
        {
            yield break;
        }

        using var scope = scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettings<McpSettings>>().GetDefaultAsync().AsTask().GetAwaiter().GetResult();
        if (settings.ClientRegistration == McpClientRegistrationMode.Open)
        {
            yield return new InsecureDefault
            {
                Id = OpenClientRegistration,
                Message = "The setting mcp.clientRegistration is open in Production, so any MCP client that reaches /connect/register is approved at once with the default role. Set it to approval unless every client that can reach the instance is trusted.",
            };
        }
    }
}
