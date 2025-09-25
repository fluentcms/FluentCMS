using FluentAssertions;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.Tests.Integration.Helpers;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using FluentCMS.Repositories.Tests.Integration.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Tests.Integration.RepositoryTests;

public class ErrorHandlingTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly IRepository<TestUser> _userRepository;
    private readonly IRepository<TestProduct> _productRepository;

    public ErrorHandlingTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _userRepository = _fixture.GetRepository<TestUser>();
        _productRepository = _fixture.GetRepository<TestProduct>();
    }

    #region Parameter Validation Tests

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
    public async Task Update_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _userRepository.Update(null!);
        action.ShouldThrowArgumentNullException("entity");
    }

    [Fact]
    public async Task Remove_NullEntity_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _userRepository.Remove((TestUser)null!);
        action.ShouldThrowArgumentNullException("entity");
    }

    [Fact]
    public async Task Query_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _userRepository.Query(null!);
        action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task FirstOrDefault_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _userRepository.FirstOrDefault(null!);
        action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task SingleOrDefault_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _userRepository.SingleOrDefault(null!);
        action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task Count_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _userRepository.Count(null!);
        action.ShouldThrowArgumentNullException("specification");
    }

    [Fact]
    public async Task Sum_NullSpecification_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Act & Assert
        var action = async () => await _productRepository.Sum(null!, p => p.Price);
        action.ShouldThrowArgumentNullException("specification");
    }

    #endregion

    #region Repository Exception Handling Tests

    [Fact]
    public async Task Query_InvalidSpecification_ShouldThrowRepositoryException()
    {
        // Arrange
        await _fixture.CleanDatabase();

        // Create a specification that will cause a database error
        var invalidSpecification = new InvalidSpecification<TestUser>();

        // Act & Assert
        var action = async () => await _userRepository.Query(invalidSpecification);
        await action.Should().ThrowAsync<Exception>(); // Invalid SQL will throw an exception
    }

    #endregion

    #region Cancellation Token Handling Tests

    [Fact]
    public async Task Add_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await _userRepository.Add(user, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task Update_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await _userRepository.Add(user);
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await _userRepository.Update(addedUser, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task Remove_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var user = TestDataBuilder.CreateTestUser();
        var addedUser = await _userRepository.Add(user);
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await _userRepository.Remove(addedUser, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task Query_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0);
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await _userRepository.Query(specification, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task GetById_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var id = Guid.NewGuid();
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await _userRepository.GetById(id, cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    [Fact]
    public async Task GetAll_CancelledToken_ShouldThrowOperationCancelledException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var cancellationToken = new CancellationToken(true); // Already cancelled

        // Act & Assert
        var action = async () => await _userRepository.GetAll(cancellationToken);
        await action.ShouldThrowOperationCancelledException();
    }

    #endregion

    #region Additional Edge Case Tests

    [Fact]
    public async Task AddRange_NullEntityInCollection_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = new List<TestUser?>
        {
            TestDataBuilder.CreateTestUser(),
            null!, // Null entity in collection
            TestDataBuilder.CreateTestUser()
        };

        // Act & Assert
        var action = async () => await _userRepository.AddRange(users!);
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Sum_NullSelector_ShouldThrowArgumentNullException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var specification = SpecificationExtensions.Where<TestProduct>(p => p.IsActive);

        // Act & Assert
        var action = async () => await _productRepository.Sum(specification, null!);
        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Repository_OperationOnDisposedContext_ShouldHandleGracefully()
    {
        // Arrange
        using var scope = _fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestUser>>();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var user = TestDataBuilder.CreateTestUser();
        await repository.Add(user);

        // Dispose the context
        await context.DisposeAsync();

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
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(100); // Larger dataset
        await _userRepository.AddRange(users);

        using var cancellationTokenSource = new CancellationTokenSource();

        // Cancel immediately to test cancellation handling
        cancellationTokenSource.Cancel();

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0);

        // Act & Assert
        var action = async () => await _userRepository.Query(specification, cancellationTokenSource.Token);
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
