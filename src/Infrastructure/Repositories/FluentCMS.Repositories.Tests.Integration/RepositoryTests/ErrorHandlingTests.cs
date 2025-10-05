//using FluentAssertions;
//using FluentCMS.Repositories.Abstractions;
//using FluentCMS.Repositories.Tests.Integration.Helpers;
//using FluentCMS.Repositories.Tests.Integration.TestEntities;
//using FluentCMS.Repositories.Tests.Integration.TestFixtures;

//namespace FluentCMS.Repositories.Tests.Integration.RepositoryTests;

///// <summary>
///// Error handling tests using isolated database instances for each test.
///// Each test gets its own fresh database, ensuring complete test isolation.
///// </summary>
//public class ErrorHandlingTests : IClassFixture<IsolatedSqliteTestFixture>
//{
//    private readonly IsolatedSqliteTestFixture _fixture;

//    public ErrorHandlingTests(IsolatedSqliteTestFixture fixture)
//    {
//        _fixture = fixture;
//    }

//    #region Parameter Validation Tests

//    [Fact]
//    public async Task Add_NullEntity_ShouldThrowArgumentNullException()
//    {
//        // Arrange - Each test gets its own isolated database
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Act & Assert
//        var action = async () => await userRepository.Add(null!);
//        await action.ShouldThrowArgumentNullException("entity");
//    }

//    [Fact]
//    public async Task Update_NullEntity_ShouldThrowArgumentNullException()
//    {
//        // Arrange - Fresh isolated database
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Act & Assert
//        var action = async () => await userRepository.Update(null!);
//        await action.ShouldThrowArgumentNullException("entity");
//    }

//    [Fact]
//    public async Task Remove_NullEntity_ShouldThrowArgumentNullException()
//    {
//        // Arrange - Isolated test environment
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Act & Assert
//        var action = async () => await userRepository.Remove((TestUser)null!);
//        await action.ShouldThrowArgumentNullException("entity");
//    }

//    [Fact]
//    public async Task Query_NullSpecification_ShouldThrowArgumentNullException()
//    {
//        // Arrange
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Act & Assert
//        var action = async () => await userRepository.Query().Where(null!).ToList();
//        await action.ShouldThrowArgumentNullException("specification");
//    }

//    [Fact]
//    public async Task FirstOrDefault_NullSpecification_ShouldThrowArgumentNullException()
//    {
//        // Arrange
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Act & Assert
//        var action = async () => await userRepository.Query().FirstOrDefault(null!);
//        await action.ShouldThrowArgumentNullException("specification");
//    }

//    [Fact]
//    public async Task SingleOrDefault_NullSpecification_ShouldThrowArgumentNullException()
//    {
//        // Arrange
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Act & Assert
//        var action = async () => await userRepository.Query().SingleOrDefault(null!);
//        await action.ShouldThrowArgumentNullException("specification");
//    }

//    [Fact]
//    public async Task Count_NullSpecification_ShouldThrowArgumentNullException()
//    {
//        // Arrange
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Act & Assert
//        var action = async () => await userRepository.Query().Count(null!);
//        await action.ShouldThrowArgumentNullException("specification");
//    }

//    #endregion

//    #region Additional Edge Case Tests

//    [Fact]
//    public async Task AddRange_NullEntityInCollection_ShouldThrowArgumentNullException()
//    {
//        // Arrange - Isolated database
//        using var scope = _fixture.CreateIsolatedScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        var users = new List<TestUser?>
//        {
//            TestDataBuilder.CreateTestUser(),
//            null!, // Null entity in collection
//            TestDataBuilder.CreateTestUser()
//        };

//        // Act & Assert
//        var action = async () => await userRepository.AddRange(users!);
//        await action.Should().ThrowAsync<ArgumentNullException>();
//    }

//    [Fact]
//    public async Task Repository_OperationOnDisposedContext_ShouldHandleGracefully()
//    {
//        // Arrange - Isolated test environment
//        IRepository<TestUser> repository;
//        using (var scope = _fixture.CreateIsolatedScope())
//        {
//            repository = scope.GetRepository<TestUser>();
//            var context = scope.GetDbContext();
//            var user = TestDataBuilder.CreateTestUser();
//            await repository.Add(user);
//        }

//        // Act & Assert - Operations should throw meaningful exceptions
//        var addAction = async () => await repository.Add(TestDataBuilder.CreateTestUser());
//        var getAllAction = async () => await repository.GetAll();
//        var getByIdAction = async () => await repository.GetById(Guid.NewGuid());

//        await addAction.Should().ThrowAsync<Exception>();
//        await getAllAction.Should().ThrowAsync<Exception>();
//        await getByIdAction.Should().ThrowAsync<Exception>();
//    }

//    #endregion
//}
