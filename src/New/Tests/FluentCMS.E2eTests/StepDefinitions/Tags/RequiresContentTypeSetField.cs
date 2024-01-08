namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresContentTypeSetField", Order = 60)]
    public async Task RequiresContentTypeSetField()
    {
        GivenIHaveAContentType();
        await WhenISetAFieldAsync();
        await ThenIShouldSeeTheFieldAsync();
    }
}
