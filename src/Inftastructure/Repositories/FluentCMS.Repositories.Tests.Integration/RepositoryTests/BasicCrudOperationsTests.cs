using FluentAssertions;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.Tests.Integration.Helpers;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using FluentCMS.Repositories.Tests.Integration.TestFixtures;

namespace FluentCMS.Repositories.Tests.Integration.RepositoryTests;

public class BasicCrudOperationsTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly IRepository<TestUser> _userRepository;

    public BasicCrudOperationsTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _userRepository = _fixture.GetRepository<TestUser>();
    }

    #region Add Operations Tests

    [Fact]
    public async Task Add_ValidEntity_ShouldGenerateIdAndReturnEntity()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUserWithEmptyId();

        // Act
        var result = await _userRepository.Add(user);

        // Assert
        result.Should().NotBeNull();
        result.ShouldHaveValidGeneratedId();
        result.Name.Should().Be(user.Name);
        result.Email.Should().Be(user.Email);
        result.Age.Should().Be(user.Age);

        // Verify it's persisted
        var retrievedUser = await _userRepository.GetById(result.Id);
        retrievedUser.Should().NotBeNull();
        retrievedUser!.ShouldBeEquivalentToTestUser(result);
    }

    [Fact]
    public async Task Add_EntityWithEmptyId_ShouldGenerateNewId()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUserWithEmptyId();
        user.Id.Should().Be(Guid.Empty);

        // Act
        var result = await _userRepository.Add(user);

        // Assert
        result.ShouldHaveValidGeneratedId();
        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Add_EntityWithExistingId_ShouldKeepExistingId()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var existingId = Guid.NewGuid();
        var user = TestDataBuilder.CreateTestUser(id: existingId);

        // Act
        var result = await _userRepository.Add(user);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(existingId);
    }

    [Fact]
    public async Task Add_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _userRepository.Add(null!);
        action.ShouldThrowArgumentNullException("entity");
    }

    [Fact]
    public async Task AddRange_ValidEntities_ShouldAddAllAndReturnList()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsersWithEmptyIds(3);

        // Act
        var result = await _userRepository.AddRange(users);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(3);
        resultList.Should().AllSatisfy(u => u.Id.Should().NotBe(Guid.Empty));

        // Verify all are persisted
        var allUsers = await _userRepository.GetAll();
        allUsers.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddRange_EmptyList_ShouldReturnEmptyList()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var emptyUsers = new List<TestUser>();

        // Act
        var result = await _userRepository.AddRange(emptyUsers);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task AddRange_NullList_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        Func<Task> act = () => _userRepository.AddRange(null!);

        await act.Should()
                 .ThrowAsync<ArgumentNullException>()
                 .WithParameterName("entities");

    }

    [Fact]
    public async Task AddRange_EntitiesWithMixedIds_ShouldGenerateIdsForEmptyOnes()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var existingId = Guid.NewGuid();
        var users = new List<TestUser>
        {
            TestDataBuilder.CreateTestUser(id: existingId), // Has ID
            TestDataBuilder.CreateTestUserWithEmptyId(), // Empty ID
            TestDataBuilder.CreateTestUserWithEmptyId()  // Empty ID
        };

        // Act
        var result = await _userRepository.AddRange(users);

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
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await _userRepository.Add(user);

        // Modify the entity
        addedUser.Name = "Updated Name";
        addedUser.Age = 99;

        // Act
        var result = await _userRepository.Update(addedUser);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Updated Name");
        result.Age.Should().Be(99);

        // Verify changes are persisted
        var retrievedUser = await _userRepository.GetById(addedUser.Id);
        retrievedUser.Should().NotBeNull();
        retrievedUser!.Name.Should().Be("Updated Name");
        retrievedUser.Age.Should().Be(99);
    }

    [Fact]
    public async Task Update_NonExistingEntity_ShouldThrowException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();

        // Act & Assert
        var act = async () => await _userRepository.Update(user);
        await act.ShouldThrowRepositoryException<TestUser>();
    }

    [Fact]
    public async Task Update_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        Func<Task> act = () => _userRepository.Update(null!);

        await act.Should()
                 .ThrowAsync<ArgumentNullException>()
                 .WithParameterName("entity");
    }

    [Fact]
    public async Task Update_EntityAfterDetachment_ShouldWork()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await _userRepository.Add(user);

        // The repository detaches entities, so this should work
        addedUser.Name = "Updated After Detachment";

        // Act
        var result = await _userRepository.Update(addedUser);

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
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await _userRepository.Add(user);

        // Act
        var result = await _userRepository.Remove(addedUser);

        // Assert
        result.Should().NotBeNull();
        result!.ShouldBeEquivalentToTestUser(addedUser);

        // Verify it's removed from database
        var retrievedUser = await _userRepository.GetById(addedUser.Id);
        retrievedUser.Should().BeNull();
    }

    [Fact]
    public async Task Remove_ExistingEntityById_ShouldRemoveAndReturnEntity()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await _userRepository.Add(user);

        // Act
        var result = await _userRepository.Remove(addedUser.Id);

        // Assert
        result.Should().NotBeNull();
        result!.ShouldBeEquivalentToTestUser(addedUser);

        // Verify it's removed from database
        var retrievedUser = await _userRepository.GetById(addedUser.Id);
        retrievedUser.Should().BeNull();
    }

    [Fact]
    public async Task Remove_NonExistingEntityById_ShouldReturnNull()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _userRepository.Remove(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    //[Fact]
    //public async Task Remove_NonExistingEntityByObject_ShouldReturnNull()
    //{
    //    // Arrange
    //    await _fixture.CleanDatabase();
    //    var user = TestDataBuilder.CreateTestUser();

    //    // Act (entity doesn't exist in database)
    //    var result = await _userRepository.Remove(user);

    //    // Assert
    //    result.Should().BeNull();
    //}

    [Fact]
    public async Task Remove_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        Func<Task> act = () => _userRepository.Remove(null!);

        await act.Should()
                 .ThrowAsync<ArgumentNullException>()
                 .WithParameterName("entity");
    }

    [Fact]
    public async Task Remove_EmptyGuid_ShouldReturnNull()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act
        var result = await _userRepository.Remove(Guid.Empty);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Get Operations Tests

    [Fact]
    public async Task GetById_ExistingEntity_ShouldReturnEntity()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await _userRepository.Add(user);

        // Act
        var result = await _userRepository.GetById(addedUser.Id);

        // Assert
        result.Should().NotBeNull();
        result!.ShouldBeEquivalentToTestUser(addedUser);
    }

    [Fact]
    public async Task GetById_NonExistingEntity_ShouldReturnNull()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _userRepository.GetById(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetById_EmptyGuid_ShouldReturnNull()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act
        var result = await _userRepository.GetById(Guid.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_WithData_ShouldReturnAllEntities()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(5);
        await _userRepository.AddRange(users);

        // Act
        var result = await _userRepository.GetAll();

        // Assert
        result.Should().HaveCount(5);
        var resultList = result.ToList();
        resultList.Should().AllSatisfy(u => u.Id.Should().NotBe(Guid.Empty));
    }

    #endregion
}
