namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I update a user")]
    public async Task WhenIUpdateAUserAsync()
    {
        var userClient = context.Get<UserClient>();
        var userId = context.Get<UserResponseIApiResult>().Data.Id;
        var updateBody = new UserUpdateRequest() {
            Id = userId,
            Email = "UpdatedDummyEmail@localhost",
        };
        var result = await userClient.UpdateAsync(updateBody);
        context.Set(result);
    }
}
