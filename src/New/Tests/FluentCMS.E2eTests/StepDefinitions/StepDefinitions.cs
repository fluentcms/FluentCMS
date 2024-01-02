using FluentCMS.E2eTests.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.E2eTests.StepDefinitions;
[Binding]
public partial class StepDefinitions(ScenarioContext context)
{
    [Before(Order = 0)]
    public void RegisterServices()
    {
        var services = new ServiceCollection().ConfigureServices();
        context[ScenarioContextExtensions.ServiceProviderKey] = services.BuildServiceProvider();
    }

}
