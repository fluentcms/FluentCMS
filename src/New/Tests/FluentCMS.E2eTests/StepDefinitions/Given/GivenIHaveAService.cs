using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have a(n) {string}")]
    public void GivenIHaveAService(string serviceName)
    {
        // find service type byName
        var definedTypes = Assembly.GetAssembly(typeof(ClientServiceExtensions))!.DefinedTypes;
        var serviceType = definedTypes.Single(x => x.Name == serviceName);
        context[serviceType.FullName]=(context.GetServiceProvider().GetRequiredService(serviceType));
    }
}
