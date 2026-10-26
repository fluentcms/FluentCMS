using FluentCMS.Core.Security;
using FluentCMS.Infrastructure;
using FluentCMS.Tests.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Webhooks;

// The delivery job lives in Infrastructure, so it comes with it. The database is never opened: a payload the job cannot read fails
// before any read.
public sealed class WebhookDeliveryJobConformanceTests : JobHandlerConformanceTests
{
    protected override string JobType => "webhook.deliver";

    protected override void AddHandlers(IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, NoSecrets>();
        services.AddFluentCmsInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Data Source=unused.db" })
            .Build());
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string purpose, string secret) => throw new NotSupportedException("The conformance checks protect no secrets.");

        public string Unprotect(string purpose, string protectedSecret) => throw new NotSupportedException("The conformance checks protect no secrets.");
    }
}
