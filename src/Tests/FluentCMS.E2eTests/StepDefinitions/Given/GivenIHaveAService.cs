using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Reflection;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have a(n) {string}")]
    public void GivenIHaveAService(string serviceName)
    {
        // find service type byName
        var definedTypes = Assembly.GetAssembly(typeof(ClientServiceExtensions))!.DefinedTypes;
        var serviceType = definedTypes.Single(x => x.Name == serviceName);
        var service = (context.GetServiceProvider().GetRequiredService(serviceType));

        // authorize if service is IApiClient and Token is available
        if (serviceType.IsAssignableTo(typeof(IApiClient)))
        {
            if (context.ContainsKey(typeof(UserLoginResponseIApiResult).FullName))
            {
                var loginResponse = context.Get<UserLoginResponseIApiResult>();
                var token = loginResponse.Data.Token;

                var fieldInfo = serviceType.GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                var value = fieldInfo?.GetValue(service);
                HttpClient httpClient = ((HttpClient?)value) ?? throw new Exception("Could not find _httpClient");
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
            }
        }
        context[serviceType.FullName] = service;
    }
}
