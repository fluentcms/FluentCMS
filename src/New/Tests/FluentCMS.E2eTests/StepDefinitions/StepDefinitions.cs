using FluentCMS.E2eTests.ApiClients;
using FluentCMS.E2eTests.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
