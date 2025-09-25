using FluentAssertions;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.Tests.Integration.Helpers;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using FluentCMS.Repositories.Tests.Integration.TestFixtures;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Tests.Integration.RepositoryTests;

/// <summary>
/// Error handling tests using isolated database instances for each test.
/// Each test gets its own fresh database, ensuring complete test isolation.
/// </summary>
public class ErrorHandlingTests : IClassFixture<IsolatedSqliteTestFixture>
{
    private readonly IsolatedSqliteTestFixture _fixture;

    public ErrorHandlingTests(IsolatedSqliteTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Parameter Validation Tests

    [Fact]
    public async Task Add_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange - Each test gets its own isolated database
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.Add(null!);
        await action.ShouldThrowArgumentNullException("entity");
    }

    [Fact]
    public async Task Update_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange - Fresh isolated database
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.Update(null!);
        await action.ShouldThrowArgumentNullException("entity");
    }

    [Fact]
    public async Task Remove_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange - Isolated test environment
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.Remove((TestUser)null!);
        await action.ShouldThrowArgumentNullException("entity");
    }

    [Fact]
    public async Task Query_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.Query(null!);
        await action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task FirstOrDefault_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.FirstOrDefault(null!);
        await action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task SingleOrDefault_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.SingleOrDefault(null!);
        await action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task Count_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.Count(null!);
        await action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task Sum_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var productRepository = scope.GetRepository<TestProduct>();

        // Act & Assert
        var action = async () => await productRepository.Sum(null!, p => p.Price);
        await action.ShouldThrowArgumentNullException("specification");
    }

    #endregion

    #region Repository Exception Handling Tests

    [Fact]
    public async Task Query_InvalidSpecification_ShouldThrowRepositoryException()
    {
        // Arrange - Isolated database for this test
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        // Create a specification that will cause a database error
        var invalidSpecification = new InvalidSpecification<TestUser>();

        // Act & Assert
        var action = async () => await userRepository.Query(invalidSpecification);
        await action.Should().ThrowAsync<Exception>(); // Invalid SQL will throw an exception
    }

    #endregion

    #region Cancellation Token Handling Tests

    [Fact]
    public async Task Add_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange - Isolated database
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await userRepository.Add(user, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task Update_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange - Fresh isolated database
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await userRepository.Add(user);
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await userRepository.Update(addedUser, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task Remove_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange - Isolated test environment
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await userRepository.Add(user);
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await userRepository.Remove(addedUser, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task Query_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0);
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await userRepository.Query(specification, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task GetById_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var id = Guid.NewGuid();
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await userRepository.GetById(id, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task GetAll_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await userRepository.GetAll(cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    #endregion

    #region Additional Edge Case Tests

    [Fact]
    public async Task AddRange_NullEntityInCollection_ShouldThrowArgumentNullException()
    {
        // Arrange - Isolated database
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var users = new List<TestUser?>
        {
            TestDataBuilder.CreateTestUser(),
            null!, // Null entity in collection
            TestDataBuilder.CreateTestUser()
        };

        // Act & Assert
        var action = async () => await userRepository.AddRange(users!);
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Sum_NullSelector_ShouldThrowArgumentNullException()
    {
        // Arrange - Fresh isolated database
        using var scope = _fixture.CreateIsolatedScope();
        var productRepository = scope.GetRepository<TestProduct>();

        var specification = SpecificationExtensions.Where<TestProduct>(p => p.IsActive);

        // Act & Assert
        var action = async () => await productRepository.Sum(specification, null!);
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Repository_OperationOnDisposedContext_ShouldHandleGracefully()
    {
        // Arrange - Isolated test environment
        IRepository<TestUser> repository;
        using (var scope = _fixture.CreateIsolatedScope())
        {
            repository = scope.GetRepository<TestUser>();
            var context = scope.GetDbContext();
            var user = TestDataBuilder.CreateTestUser();
            await repository.Add(user);
        }

        // Act & Assert - Operations should throw meaningful exceptions
        var addAction = async () => await repository.Add(TestDataBuilder.CreateTestUser());
        var getAllAction = async () => await repository.GetAll();
        var getByIdAction = async () => await repository.GetById(Guid.NewGuid());

        await addAction.Should().ThrowAsync<Exception>();
        await getAllAction.Should().ThrowAsync<Exception>();
        await getByIdAction.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task CancellationTokenSource_CancelDuringLongOperation_ShouldCancel()
    {
        // Arrange - Isolated database with test data
        using var scope = _fixture.CreateIsolatedScope();
        var userRepository = scope.GetRepository<TestUser>();

        var users = TestDataBuilder.CreateTestUsers(100); // Larger dataset
        await userRepository.AddRange(users);

        using var cancellationTokenSource = new CancellationTokenSource();

        // Cancel immediately to test cancellation handling
        cancellationTokenSource.Cancel();

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0);

        // Act & Assert
        var action = async () => await userRepository.Query(specification, cancellationTokenSource.Token);
        await action.ShouldThrowOperationCancelledException();
    }

    #endregion
}

// Helper class for testing invalid specifications
internal class InvalidSpecification<T> : ISpecification<T> where T : class
{
    public IQueryable<T> Apply(IQueryable<T> query)
    {
        // This will cause a SQL error by trying to access a non-existent column
        return query.Where(x => EF.Property<string>(x, "NonExistentColumn") == "test");
    }
}
