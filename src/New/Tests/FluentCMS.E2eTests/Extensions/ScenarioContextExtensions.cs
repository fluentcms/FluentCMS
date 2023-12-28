using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.E2eTests.StepDefinitions;

public static partial class ScenarioContextExtensions
{
    public const string ServiceProviderKey = "ServiceProvider";
    public static ServiceProvider GetServiceProvider(this ScenarioContext context)
    {
        return context.Get<ServiceProvider>(ServiceProviderKey);
    }

}
