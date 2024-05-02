using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Given(@"I have a dto ""(.*)""")]
    public void GivenIHaveA(string name, Table table)
    {
        var type = typeof(IApiClient).Assembly.DefinedTypes.Single(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var obj = table.CreateInstance(() => Activator.CreateInstance(type));
        context.Add(type.FullName!, obj);
    }
}
