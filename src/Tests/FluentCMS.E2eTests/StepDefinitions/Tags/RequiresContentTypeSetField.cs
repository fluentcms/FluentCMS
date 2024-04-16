namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresContentTypeSetField", Order = 60)]
    public async Task RequiresContentTypeSetField()
    {
        GivenIHaveAContentType();
        var table = new Table("field", "value");
        table.AddRow("field", "value");
        table.AddRow("slug", "dummy-field-slug");
        table.AddRow("title", "dummy-field-title");
        table.AddRow("description", "dummy-field-description");
        table.AddRow("label", "dummy-field-label");
        table.AddRow("placeholder", "dummy-field-placeholder");
        table.AddRow("hint", "dummy-field-hint");
        table.AddRow("defaultValue", "dummy-field-defaultValue");
        table.AddRow("isRequired", "false");
        table.AddRow("isPrivate", "false");
        table.AddRow("fieldType", "dummy-field-fieldType");
        await WhenISetAFieldAsync(table);
        await ThenIShouldSeeTheFieldAsync();
    }
}
