namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("App Create result should be Success")]
    public void ThenAppCreateResultShouldBeSuccess()
    {
        var appApiResponse = context.Get<AppDetailResponseIApiResult>();

        appApiResponse.Errors.ShouldBeEmpty();
        appApiResponse.Data.ShouldNotBeNull();
        appApiResponse.Data.Id.ShouldNotBe(default);

        context.Set(appApiResponse);
        context.Set(appApiResponse.Data.Id, "appId");
    }
}
