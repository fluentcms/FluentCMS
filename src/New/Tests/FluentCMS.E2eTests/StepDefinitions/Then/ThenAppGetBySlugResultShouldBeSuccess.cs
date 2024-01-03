namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("App GetBySlug result should be Success")]
    public void ThenAppGetBySlugResultShouldBeSuccess()
    {
        var appGetBySlugResult = context.Get<AppResponseIApiResult>();
        var slug = context.Get<string>("appSlug");
        appGetBySlugResult.Errors.ShouldBeEmpty();
        appGetBySlugResult.Data.ShouldNotBeNull();
        appGetBySlugResult.Data.Id.ShouldNotBe(default);
        appGetBySlugResult.Data.Slug.ShouldBe(slug);
    }
}
