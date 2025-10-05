using FluentAssertions;
using FluentCMS.Repositories.Tests.Integration.Helpers;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using FluentCMS.Repositories.Tests.Integration.TestFixtures;

namespace FluentCMS.Repositories.Tests.Integration.RepositoryTests;

/// <summary>
/// Improved version of BasicCrudOperationsTests that demonstrates proper test isolation.
/// Each test gets its own isolated database instance, eliminating cross-test contamination.
/// Compare this with the original BasicCrudOperationsTests to see the improvements.
/// </summary>
public class BasicCrudOperationsTests(IsolatedSqliteTestFixture fixture) : IClassFixture<IsolatedSqliteTestFixture>
{

    #region Add Operations Tests

    [Fact]
    public async Task Add_ValidEntity_ShouldGenerateIdAndReturnEntity()
    {
        // Arrange - Each test gets its own isolated database
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUserWithEmptyId();

        // Act
        var result = await userRepository.Add(user);

        // Assert
        result.Should().NotBeNull();
        result.ShouldHaveValidGeneratedId();
        result.Name.Should().Be(user.Name);
        result.Email.Should().Be(user.Email);
        result.Age.Should().Be(user.Age);

        // Verify it's persisted
        var retrievedUser = await userRepository.GetById(result.Id);
        retrievedUser.Should().NotBeNull();
        retrievedUser!.ShouldBeEquivalentToTestUser(result);
    }

    [Fact]
    public async Task Add_EntityWithEmptyId_ShouldGenerateNewId()
    {
        // Arrange - Fresh database for this test
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUserWithEmptyId();
        user.Id.Should().Be(Guid.Empty);

        // Act
        var result = await userRepository.Add(user);

        // Assert
        result.ShouldHaveValidGeneratedId();
        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Add_EntityWithExistingId_ShouldKeepExistingId()
    {
        // Arrange - Isolated test environment
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var existingId = Guid.NewGuid();
        var user = TestDataBuilder.CreateTestUser(id: existingId);

        // Act
        var result = await userRepository.Add(user);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(existingId);
    }

    [Fact]
    public async Task Add_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.Add(null!);
        await action.ShouldThrowArgumentNullException("entity");
    }

    [Fact]
    public async Task AddRange_ValidEntities_ShouldAddAllAndReturnList()
    {
        // Arrange - Clean isolated database
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var users = TestDataBuilder.CreateTestUsersWithEmptyIds(3);

        // Act
        var result = await userRepository.AddRange(users);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(3);
        resultList.Should().AllSatisfy(u => u.Id.Should().NotBe(Guid.Empty));

        // Verify all are persisted
        var allUsers = await userRepository.GetAll();
        allUsers.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddRange_EmptyList_ShouldReturnEmptyList()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var emptyUsers = new List<TestUser>();

        // Act
        var result = await userRepository.AddRange(emptyUsers);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddRange_NullList_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        Func<Task> act = () => userRepository.AddRange(null!);

        await act.Should()
                 .ThrowAsync<ArgumentNullException>()
                 .WithParameterName("entities");
    }

    [Fact]
    public async Task AddRange_EntitiesWithMixedIds_ShouldGenerateIdsForEmptyOnes()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var existingId = Guid.NewGuid();
        var users = new List<TestUser>
        {
            TestDataBuilder.CreateTestUser(id: existingId), // Has ID
            TestDataBuilder.CreateTestUserWithEmptyId(), // Empty ID
            TestDataBuilder.CreateTestUserWithEmptyId()  // Empty ID
        };

        // Act
        var result = await userRepository.AddRange(users);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(3);
        resultList[0].Id.Should().Be(existingId);
        resultList[1].Id.Should().NotBe(Guid.Empty);
        resultList[2].Id.Should().NotBe(Guid.Empty);
        resultList[1].Id.Should().NotBe(resultList[2].Id);
    }

    #endregion

    #region Update Operations Tests

    [Fact]
    public async Task Update_ExistingEntity_ShouldUpdateAndReturnEntity()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await userRepository.Add(user);

        // Modify the entity
        addedUser.Name = "Updated Name";
        addedUser.Age = 99;

        // Act
        var result = await userRepository.Update(addedUser);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Updated Name");
        result.Age.Should().Be(99);

        // Verify changes are persisted
        var retrievedUser = await userRepository.GetById(addedUser.Id);
        retrievedUser.Should().NotBeNull();
        retrievedUser!.Name.Should().Be("Updated Name");
        retrievedUser.Age.Should().Be(99);
    }

    [Fact]
    public async Task Update_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        Func<Task> act = () => userRepository.Update(null!);

        await act.Should()
                 .ThrowAsync<ArgumentNullException>()
                 .WithParameterName("entity");
    }

    [Fact]
    public async Task Update_EntityAfterDetachment_ShouldWork()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await userRepository.Add(user);

        // The repository detaches entities, so this should work
        addedUser.Name = "Updated After Detachment";

        // Act
        var result = await userRepository.Update(addedUser);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Updated After Detachment");
    }

    #endregion

    #region Remove Operations Tests

    [Fact]
    public async Task Remove_ExistingEntityByObject_ShouldRemoveAndReturnEntity()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await userRepository.Add(user);

        // Act
        var result = await userRepository.Remove(addedUser);

        // Assert
        result.Should().NotBeNull();
        result!.ShouldBeEquivalentToTestUser(addedUser);

        // Verify it's removed from database
        var retrievedUser = await userRepository.GetById(addedUser.Id);
        retrievedUser.Should().BeNull();
    }

    [Fact]
    public async Task Remove_ExistingEntity_ShouldRemoveAndReturnEntity()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await userRepository.Add(user);

        // Act
        var result = await userRepository.Remove(addedUser);

        // Assert
        result.Should().NotBeNull();
        result!.ShouldBeEquivalentToTestUser(addedUser);
    }

    [Fact]
    public async Task Remove_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        Func<Task> act = () => userRepository.Remove(null!);

        await act.Should()
                 .ThrowAsync<ArgumentNullException>()
                 .WithParameterName("entity");
    }

    #endregion

    #region Get Operations Tests

    [Fact]
    public async Task GetById_ExistingEntity_ShouldReturnEntity()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await userRepository.Add(user);

        // Act
        var result = await userRepository.GetById(addedUser.Id);

        // Assert
        result.Should().NotBeNull();
        result!.ShouldBeEquivalentToTestUser(addedUser);
    }

    [Fact]
    public async Task GetAll_WithData_ShouldReturnAllEntities()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var users = TestDataBuilder.CreateTestUsers(5);
        await userRepository.AddRange(users);

        // Act
        var result = await userRepository.GetAll();

        // Assert
        result.Should().HaveCount(5);
        var resultList = result.ToList();
        resultList.Should().AllSatisfy(u => u.Id.Should().NotBe(Guid.Empty));
    }

    [Fact]
    public async Task GetAll_EmptyDatabase_ShouldReturnEmptyList()
    {
        // Arrange - Fresh empty database
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act
        var result = await userRepository.GetAll();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Test Isolation Demonstration Tests

    [Fact]
    public async Task TestIsolation_FirstTest_ShouldStartWithEmptyDatabase()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act - Check that database is empty
        var users = await userRepository.GetAll();

        // Assert - Should always be empty regardless of other test execution
        users.Should().BeEmpty("each test should start with a clean database");
    }

    [Fact]
    public async Task TestIsolation_SecondTest_ShouldAlsoStartWithEmptyDatabase()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act - Check that database is empty
        var users = await userRepository.GetAll();

        // Assert - Should always be empty regardless of other test execution
        users.Should().BeEmpty("each test should start with a clean database");

        // Add some data to prove this test is isolated
        await userRepository.Add(TestDataBuilder.CreateTestUser());
        var usersAfterAdd = await userRepository.GetAll();
        usersAfterAdd.Should().HaveCount(1);
    }

    [Fact]
    public async Task TestIsolation_ThirdTest_ShouldNotSeeDataFromOtherTests()
    {
        // Arrange
        using var scope = fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act - Check that database is empty
        var users = await userRepository.GetAll();

        // Assert - Should be empty even though other tests added data
        users.Should().BeEmpty("test isolation ensures no cross-test contamination");
    }

    #endregion
}
