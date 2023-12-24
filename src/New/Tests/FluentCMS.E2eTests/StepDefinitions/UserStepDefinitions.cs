using FluentCMS.E2eTests.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
[Binding, Scope(Feature = "Basic User Client functionality")]
public class UserStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;

    public UserStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private ServiceProvider _serviceProvider = default!;
    private UserCreateRequest CreateUserData
    {
        get
        {
            // return fallback creation dto if nothing provided 
            return (UserCreateRequest)_scenarioContext
                .GetOrCreate(nameof(CreateUserData),
                    new UserCreateRequest()
                    {
                        Username = "DummyUser",
                        Email = "DummyUser@localhost",
                        Password = "DummyPassw0rd!",
                        RoleIds = []
                    });
        }
        set
        {
            _scenarioContext[nameof(CreateUserData)] = value;
        }
    }

    private SetupClient SetupClient => _serviceProvider.GetRequiredService<SetupClient>();
    private UserClient UserClient => _serviceProvider.GetRequiredService<UserClient>();

    public UserResponseIApiResult CreateResponse
    {
        get
        {
            return (UserResponseIApiResult)_scenarioContext[nameof(CreateResponse)];
        }
        set
        {
            _scenarioContext[nameof(CreateResponse)] = value;
        }
    }
    public UserResponse UpdateUserResponse
    {
        get
        {
            return (UserResponse)_scenarioContext[nameof(UpdateUserResponse)];
        }
        set
        {
            _scenarioContext[nameof(UpdateUserResponse)] = value;
        }
    }

    public UserResponseIApiResult GetUserWithId
    {
        get
        {
            return (UserResponseIApiResult)_scenarioContext[nameof(GetUserWithId)];
        }
        private set
        {
            _scenarioContext[nameof(GetUserWithId)] = value;
        }
    }

    public UserResponseIApiPagingResult AllUsers
    {
        get { return (UserResponseIApiPagingResult)_scenarioContext[nameof(AllUsers)]; }
        private set
        {
            _scenarioContext[nameof(AllUsers)] = value;
        }
    }

    [Before]
    public void Before()
    {
        // setup dependencies
        _serviceProvider = new ServiceCollection()
            .ConfigureServices()
            .BuildServiceProvider();
    }
    [Given("Reset Setup")]
    public async Task ResetSetup()
    {
        await SetupClient.ResetAsync();
    }

    [Given("Setup is initialized")]
    public async Task GivenSetupIsInitialized()
    {
        await SetupClient.StartAsync();
    }

    [Given("Dummy Data for User Creation")]
    public void GivenDummyDataForUserCreation(Table table)
    {
        CreateUserData = table.CreateInstance<UserCreateRequest>();
    }

    [When("I create a user")]
    public async Task WhenICreateAUser()
    {
        CreateResponse = await UserClient.CreateAsync(CreateUserData);
    }

    [Then("user is created")]
    public void ThenUserIsCreated()
    {
        CreateResponse.Data.ShouldNotBeNull();
        CreateResponse.Data.Id.ShouldNotBe(default);
    }

    [When("I get a user with id")]
    public async Task WhenIGetAUserWithId()
    {
        GetUserWithId = await UserClient.GetAsync(CreateResponse.Data.Id);
    }

    [Then("user is returned")]
    public void ThenUserIsReturned()
    {
        GetUserWithId.Data.ShouldNotBeNull();
        GetUserWithId.Data.Id.ShouldBe(CreateResponse.Data.Id);
    }

    [When("I get all users")]
    public async Task WhenIGetAllUsers()
    {
        AllUsers = await UserClient.GetAllAsync();
    }

    [Then("all users are returned")]
    public void ThenAllUsersAreReturned()
    {
        AllUsers.Data.ShouldNotBeNull();
        AllUsers.Data.ShouldNotBeEmpty();
    }

    [When("I update a user")]
    public async Task WhenIUpdateAUser()
    {
        UpdateUserResponse = (await UserClient.UpdateAsync(new UserUpdateRequest() { Id = CreateResponse.Data.Id, Email = "UpdatedDummyUser@localhost", RoleIds = [Guid.NewGuid()] })).Data;
    }

    [Then("user is updated")]
    public void ThenUserIsUpdated()
    {
        UpdateUserResponse.Id.ShouldBe(CreateResponse.Data.Id);
        UpdateUserResponse.Email.ShouldBe("UpdatedDummyUser@localhost");
        // how to check roles?
    }

}
