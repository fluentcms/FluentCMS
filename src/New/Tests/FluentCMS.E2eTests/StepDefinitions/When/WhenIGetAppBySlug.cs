namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Get App By Slug {string}")]
    public async Task WhenIGetAppBySlugAsync(string slug)
    {
        var appClient = context.Get<AppClient>();
        context.Set(slug, "appSlug");
        var app = await appClient.GetBySlugAsync(slug);
        context.Set(app);


    }
}
