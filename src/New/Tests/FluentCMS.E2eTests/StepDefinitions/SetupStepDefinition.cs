using FluentCMS.E2eTests.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
[Binding, Scope(Feature = "Basic functionality Test of Host API client")]
public class SetupStepDefinition
{
    private IServiceProvider _serviceProvider = default!;
    private ISetupClient _client = default!;
    private BooleanIApiResult _isInitialized;

    [Before]
    public void Before()
    {
        // setup dependencies
        _serviceProvider = new ServiceCollection()
            .ConfigureServices()
            .BuildServiceProvider();

    }
    [Given("I have a SetupClient")]
    public void GivenIHaveASetupClient()
    {
        _client = _serviceProvider.GetRequiredService<SetupClient>();
    }

    [Then("Reset Setup")]
    public async Task ThenResetSetup()
    {
        await _client.ResetAsync();
    }

    [When("I get setup IsInitialized")]
    public async Task WhenIGetSetupStatus()
    {
        _isInitialized = await _client.IsInitializedAsync();
    }

    [Then("Setup initialization status should be {string}")]
    public void ThenSetupInitializationStatusShouldBe(string status)
    {
        _isInitialized.Data.ShouldBe(status.Equals("true", comparisonType: StringComparison.InvariantCultureIgnoreCase));
    }

    [When("I Start the host")]
    public async void GivenIStartTheHost()
    {
        await _client.StartAsync();
    }

    [Then("Wait a Bit {int}sec(s)")]
    public async Task ThenWaitABit(int p0)
    {
        await Task.Delay(p0 * 1000);
    }

}
