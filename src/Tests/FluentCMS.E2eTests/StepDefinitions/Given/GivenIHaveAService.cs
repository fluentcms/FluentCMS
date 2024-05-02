using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Reflection;
using FluentCMS.Web.UI.Services;

namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Given("I have a(n) {string}")]
    public void GivenIHaveAService(string serviceName)
    {
        // get available types
        var definedTypes = Assembly.GetAssembly(typeof(IApiClient))!.DefinedTypes;
        // find requested type
        var serviceType = (TypeInfo)definedTypes.Single(x => x.Name == serviceName);

        // if type is client
        if (serviceType.IsAssignableTo(typeof(IApiClient)))
        {
            // get an instance of IHttpClientFactory
            var httpClientFactory = context.GetServiceProvider().GetRequiredService<IHttpClientFactory>();

            // get latest login response
            var loggedIn = context.TryGetValue(typeof(UserLoginResponse).FullName, out UserLoginResponse loginResponse);

            // get HttpClientFactoryHelper.CreateApiClient<T>(UserLoginResponse) extension method
            var helperMethod = typeof(HttpClientFactoryHelper).GetMethod(nameof(HttpClientFactoryHelper.CreateApiClient), genericParameterCount: 1, types: [typeof(IHttpClientFactory), typeof(UserLoginResponse)]);

            // create & keep client
            var client = helperMethod?.MakeGenericMethod(serviceType).Invoke(null, [httpClientFactory, loggedIn ? loginResponse : null]) as IApiClient;
            context[serviceType.FullName!] = client;
        }
        else
        {
            var service = (context.GetServiceProvider().GetRequiredService(serviceType));
            context[serviceType.FullName!] = service;
        }
    }
}
