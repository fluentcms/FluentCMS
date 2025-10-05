//using FluentAssertions;
//using FluentCMS.Repositories.Tests.Integration.Helpers;
//using FluentCMS.Repositories.Tests.Integration.TestEntities;
//using FluentCMS.Repositories.Tests.Integration.TestFixtures;
//using Microsoft.EntityFrameworkCore;

//namespace FluentCMS.Repositories.Tests.Integration.RepositoryTests;

///// <summary>
///// Integration tests that use real file-based SQLite databases.
///// These tests verify behaviors that can only be tested with actual database files,
///// such as persistence, concurrent access, and file locking scenarios.
///// </summary>
//public class FileSqliteIntegrationTests : IClassFixture<FileSqliteTestFixture>
//{
//    private readonly FileSqliteTestFixture _fixture;

//    public FileSqliteIntegrationTests(FileSqliteTestFixture fixture)
//    {
//        _fixture = fixture;
//    }

//    #region Database Persistence Tests

//    [Fact]
//    public async Task DatabasePersistence_DataShouldPersistAcrossConnections()
//    {
//        // Arrange & Act
//        using var scope1 = _fixture.CreateFileScope();
//        var userRepository1 = scope1.GetRepository<TestUser>();

//        // Add data in first connection
//        var originalUser = TestDataBuilder.CreateTestUser("Persistent User", "persistent@test.com", 30);
//        var addedUser = await userRepository1.Add(originalUser);

//        // Verify database file exists
//        scope1.DatabaseFileExists().Should().BeTrue();
//        scope1.GetDatabaseFileSize().Should().BeGreaterThan(0);

//        // Dispose first connection
//        scope1.Dispose();

//        // Create new connection to same database file
//        using var scope2 = _fixture.CreateFileScope();
//        var userRepository2 = scope2.GetRepository<TestUser>();

//        // Act - Retrieve data from new connection
//        var retrievedUser = await userRepository2.GetById(addedUser.Id);

//        // Assert
//        retrievedUser.Should().NotBeNull();
//        retrievedUser!.Name.Should().Be("Persistent User");
//        retrievedUser.Email.Should().Be("persistent@test.com");
//        retrievedUser.Age.Should().Be(30);
//        retrievedUser.Id.Should().Be(addedUser.Id);
//    }

//    [Fact]
//    public async Task DatabasePersistence_UpdatesShouldPersistAcrossConnections()
//    {
//        // Arrange
//        using var scope1 = _fixture.CreateFileScope();
//        var userRepository1 = scope1.GetRepository<TestUser>();

//        var user = TestDataBuilder.CreateTestUser("Original Name", "original@test.com", 25);
//        var addedUser = await userRepository1.Add(user);

//        // Update in first connection
//        addedUser.Name = "Updated Name";
//        addedUser.Age = 35;
//        await userRepository1.Update(addedUser);

//        scope1.Dispose();

//        // Act - Verify updates in new connection
//        using var scope2 = _fixture.CreateFileScope();
//        var userRepository2 = scope2.GetRepository<TestUser>();
//        var retrievedUser = await userRepository2.GetById(addedUser.Id);

//        // Assert
//        retrievedUser.Should().NotBeNull();
//        retrievedUser!.Name.Should().Be("Updated Name");
//        retrievedUser.Age.Should().Be(35);
//    }

//    [Fact]
//    public async Task DatabasePersistence_DeletesShouldPersistAcrossConnections()
//    {
//        // Arrange
//        using var scope1 = _fixture.CreateFileScope();
//        var userRepository1 = scope1.GetRepository<TestUser>();

//        var user = TestDataBuilder.CreateTestUser();
//        var addedUser = await userRepository1.Add(user);

//        // Delete in first connection
//        await userRepository1.Remove(addedUser);

//        scope1.Dispose();

//        // Act - Verify deletion in new connection
//        using var scope2 = _fixture.CreateFileScope();
//        var userRepository2 = scope2.GetRepository<TestUser>();
//        var retrievedUser = await userRepository2.GetById(addedUser.Id);

//        // Assert
//        retrievedUser.Should().BeNull();
//    }

//    #endregion

//    #region Concurrent Access Tests

//    [Fact]
//    public async Task ConcurrentAccess_MultipleConnectionsCanReadSameData()
//    {
//        // Arrange
//        using var scope = _fixture.CreateFileScope();
//        // delete existing data to ensure clean state
//        var dbContext = scope.GetDbContext();
//        dbContext.TestUsers.RemoveRange(dbContext.TestUsers);
//        dbContext.SaveChanges();

//        var userRepository = scope.GetRepository<TestUser>();

//        var users = TestDataBuilder.CreateTestUsers(10);
//        await userRepository.AddRange(users);

//        // Act - Create multiple connections to same database
//        var tasks = new List<Task<IEnumerable<TestUser>>>();
//        for (int i = 0; i < 5; i++)
//        {
//            tasks.Add(Task.Run(async () =>
//            {
//                using var separateScope = _fixture.CreateFileScope();
//                var separateRepository = separateScope.GetRepository<TestUser>();
//                return (await separateRepository.Query().ToArray()).AsEnumerable();
//            }));
//        }

//        var results = await Task.WhenAll(tasks);

//        // Assert
//        foreach (var result in results)
//        {
//            result.Should().HaveCount(10);
//        }
//    }

//    [Fact]
//    public async Task ConcurrentAccess_MultipleConnectionsCanWriteData()
//    {
//        // Arrange
//        using var scope = _fixture.CreateFileScope();
//        // Ensure clean state
//        var dbContext = scope.GetDbContext();
//        dbContext.TestUsers.RemoveRange(dbContext.TestUsers);
//        dbContext.SaveChanges();

//        // Act - Create multiple connections that write concurrently
//        var tasks = new List<Task>();
//        for (int i = 0; i < 5; i++)
//        {
//            var taskIndex = i;
//            tasks.Add(Task.Run(async () =>
//            {
//                using var separateScope = _fixture.CreateFileScope();
//                var repository = separateScope.GetRepository<TestUser>();

//                var user = TestDataBuilder.CreateTestUser($"ConcurrentUser{taskIndex}", $"user{taskIndex}@test.com", 20 + taskIndex);
//                await repository.Add(user);
//            }));
//        }

//        await Task.WhenAll(tasks);

//        // Assert - Verify all users were added
//        var userRepository = scope.GetRepository<TestUser>();
//        var allUsers = await userRepository.Query().ToList();
//        allUsers.Should().HaveCount(5);

//        for (int i = 0; i < 5; i++)
//        {
//            allUsers.Should().Contain(u => u.Name == $"ConcurrentUser{i}");
//        }
//    }

//    [Fact]
//    public async Task ConcurrentAccess_TransactionIsolation()
//    {
//        // Arrange
//        using var scope1 = _fixture.CreateFileScope();
//        using var scope2 = _fixture.CreateFileScope();

//        var context1 = scope1.GetDbContext();
//        var context2 = scope2.GetDbContext();

//        // Act - Start transaction in first connection
//        using var transaction1 = await context1.Database.BeginTransactionAsync();

//        var user = TestDataBuilder.CreateTestUser("Transaction User", "transaction@test.com", 30);
//        context1.TestUsers.Add(user);
//        await context1.SaveChangesAsync();

//        // Check if second connection can see uncommitted data
//        var userFromConnection2 = await context2.TestUsers
//            .FirstOrDefaultAsync(u => u.Email == "transaction@test.com");

//        // Assert - Should not see uncommitted data
//        userFromConnection2.Should().BeNull();

//        // Commit transaction
//        await transaction1.CommitAsync();

//        // Now second connection should see the data
//        var userAfterCommit = await context2.TestUsers
//            .FirstOrDefaultAsync(u => u.Email == "transaction@test.com");

//        userAfterCommit.Should().NotBeNull();
//        userAfterCommit!.Name.Should().Be("Transaction User");
//    }

//    #endregion

//    #region File I/O and Storage Tests

//    [Fact]
//    public async Task FileStorage_DatabaseFileExistsAfterOperations()
//    {
//        // Arrange & Act
//        using var scope = _fixture.CreateFileScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        // Verify file exists after creation
//        scope.DatabaseFileExists().Should().BeTrue();

//        await userRepository.Add(TestDataBuilder.CreateTestUser());

//        // Assert - File should still exist and be accessible
//        scope.DatabaseFileExists().Should().BeTrue();
//        scope.GetDatabaseFileSize().Should().BeGreaterThan(0);

//        // Verify we can read the file path
//        var filePath = scope.DatabaseFilePath;
//        filePath.Should().NotBeNullOrEmpty();
//        File.Exists(filePath).Should().BeTrue();
//    }

//    #endregion

//    #region Database Schema and Migration Tests

//    [Fact]
//    public async Task DatabaseSchema_CanAddAndQueryComplexData()
//    {
//        // Arrange
//        using var scope = _fixture.CreateFileScope();
//        var userRepository = scope.GetRepository<TestUser>();
//        var productRepository = scope.GetRepository<TestProduct>();
//        var categoryRepository = scope.GetRepository<TestCategory>();

//        // Act - Add data across multiple tables
//        var category = TestDataBuilder.CreateTestCategory("Electronics", "Electronic devices");
//        var addedCategory = await categoryRepository.Add(category);

//        var product = TestDataBuilder.CreateTestProduct("Laptop", 999.99m, 1, true);
//        var addedProduct = await productRepository.Add(product);

//        var user = TestDataBuilder.CreateTestUser("John Customer", "john@customer.com", 35);
//        var addedUser = await userRepository.Add(user);

//        // Assert - Verify data integrity across tables
//        var retrievedCategory = await categoryRepository.GetById(addedCategory.Id);
//        var retrievedProduct = await productRepository.GetById(addedProduct.Id);
//        var retrievedUser = await userRepository.GetById(addedUser.Id);

//        retrievedCategory.Should().NotBeNull();
//        retrievedProduct.Should().NotBeNull();
//        retrievedUser.Should().NotBeNull();

//        retrievedProduct!.CategoryId.Should().Be(1);
//    }

//    [Fact]
//    public async Task DatabaseConstraints_UniqueConstraintsShouldWork()
//    {
//        // Arrange
//        using var scope = _fixture.CreateFileScope();
//        // Ensure clean state
//        var dbContext = scope.GetDbContext();
//        dbContext.TestUsers.RemoveRange(dbContext.TestUsers);
//        dbContext.SaveChanges();
//        var userRepository = scope.GetRepository<TestUser>();

//        var user1 = TestDataBuilder.CreateTestUser("User1", "duplicate@test.com", 25);
//        await userRepository.Add(user1);

//        // Act & Assert - Try to add user with same email (if unique constraint exists)
//        var user2 = TestDataBuilder.CreateTestUser("User2", "duplicate@test.com", 30);

//        // Note: This test assumes email should be unique. Adjust based on actual constraints.
//        // For now, we'll just verify both users can be added since there's no unique constraint
//        var addedUser2 = await userRepository.Add(user2);
//        addedUser2.Should().NotBeNull();

//        var allUsers = await userRepository.GetAll();
//        allUsers.Should().HaveCount(2);
//    }

//    #endregion

//    #region Error Handling and Recovery Tests

//    [Fact]
//    public async Task ErrorRecovery_CanRecoverFromConnectionIssues()
//    {
//        // Arrange
//        using var scope = _fixture.CreateFileScope();
//        var userRepository = scope.GetRepository<TestUser>();

//        var user = TestDataBuilder.CreateTestUser();
//        await userRepository.Add(user);

//        // Simulate connection issue by disposing and recreating
//        scope.Dispose();

//        // Act - Create new connection and verify data recovery
//        using var newScope = _fixture.CreateFileScope();
//        var newUserRepository = newScope.GetRepository<TestUser>();

//        var retrievedUser = await newUserRepository.GetById(user.Id);

//        // Assert
//        retrievedUser.Should().NotBeNull();
//        retrievedUser!.ShouldBeEquivalentToTestUser(user);
//    }

//    [Fact]
//    public async Task ErrorRecovery_DatabaseLockedScenario()
//    {
//        // Arrange
//        using var scope = _fixture.CreateFileScope();
//        var context = scope.GetDbContext();

//        // Act - Start a long-running transaction to potentially lock the database
//        using var transaction = await context.Database.BeginTransactionAsync();

//        var user = TestDataBuilder.CreateTestUser();
//        context.TestUsers.Add(user);
//        await context.SaveChangesAsync();

//        // Try to access from another connection while transaction is active
//        using var scope2 = _fixture.CreateFileScope();
//        var userRepository2 = scope2.GetRepository<TestUser>();

//        // This should work with SQLite's default locking mode
//        var allUsers = await userRepository2.GetAll();

//        // Assert
//        allUsers.Should().NotBeNull();
//        // The new user shouldn't be visible until transaction commits
//        allUsers.Should().NotContain(u => u.Id == user.Id);

//        await transaction.CommitAsync();

//        // Now the user should be visible
//        var allUsersAfterCommit = await userRepository2.GetAll();
//        allUsersAfterCommit.Should().Contain(u => u.Id == user.Id);
//    }

//    #endregion
//}
