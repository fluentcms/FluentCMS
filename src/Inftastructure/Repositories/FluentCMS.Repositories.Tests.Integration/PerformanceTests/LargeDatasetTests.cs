using FluentAssertions;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.Tests.Integration.Helpers;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using FluentCMS.Repositories.Tests.Integration.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FluentCMS.Repositories.Tests.Integration.PerformanceTests;

public class LargeDatasetTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly IRepository<TestUser> _userRepository;
    private readonly IRepository<TestProduct> _productRepository;
    private readonly ITestOutputHelper _output;

    public LargeDatasetTests(SqliteTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _userRepository = _fixture.GetRepository<TestUser>();
        _productRepository = _fixture.GetRepository<TestProduct>();
        _output = output;
    }

    #region Bulk Operations Tests

    [Fact]
    public async Task AddRange_LargeDataset_ShouldCompleteInReasonableTime()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 1000;
        var users = TestDataBuilder.CreateTestUsers(entityCount);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _userRepository.AddRange(users);

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        _output.WriteLine($"AddRange for {entityCount} entities took {elapsedMs}ms");
        
        result.Should().HaveCount(entityCount);
        elapsedMs.Should().BeLessThan(10000, "AddRange should complete within 10 seconds for 1000 entities");
        
        // Verify all entities were added
        var allUsers = await _userRepository.GetAll();
        allUsers.Should().HaveCount(entityCount);
    }

    [Fact]
    public async Task GetAll_LargeDataset_ShouldCompleteInReasonableTime()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 2000;
        var users = TestDataBuilder.CreateTestUsers(entityCount);
        await _userRepository.AddRange(users);
        
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _userRepository.GetAll();

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        _output.WriteLine($"GetAll for {entityCount} entities took {elapsedMs}ms");
        
        result.Should().HaveCount(entityCount);
        elapsedMs.Should().BeLessThan(5000, "GetAll should complete within 5 seconds for 2000 entities");
    }

    [Fact]
    public async Task Query_LargeDatasetWithFiltering_ShouldCompleteInReasonableTime()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 1500;
        var users = TestDataBuilder.CreateTestUsers(entityCount);
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age >= 30 && u.Age <= 50);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        _output.WriteLine($"Query with filtering on {entityCount} entities took {elapsedMs}ms");
        
        result.Should().NotBeEmpty();
        result.Should().OnlyContain(u => u.Age >= 30 && u.Age <= 50);
        elapsedMs.Should().BeLessThan(3000, "Filtered query should complete within 3 seconds");
    }

    [Fact]
    public async Task Count_LargeDataset_ShouldCompleteInReasonableTime()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 3000;
        var users = TestDataBuilder.CreateTestUsers(entityCount);
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 25);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _userRepository.Count(specification);

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        _output.WriteLine($"Count on {entityCount} entities took {elapsedMs}ms");
        
        result.Should().BeGreaterThan(0);
        result.Should().BeLessOrEqualTo(entityCount);
        elapsedMs.Should().BeLessThan(2000, "Count operation should complete within 2 seconds");
    }

    [Fact]
    public async Task FindPaged_LargeDataset_ShouldCompleteInReasonableTime()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 2500;
        const int pageSize = 50;
        const int pageNumber = 10;
        
        var users = TestDataBuilder.CreateTestUsers(entityCount);
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _userRepository.FindPaged(specification, pageNumber, pageSize);

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        _output.WriteLine($"FindPaged (page {pageNumber}, size {pageSize}) on {entityCount} entities took {elapsedMs}ms");
        
        result.ShouldHavePage(pageNumber);
        result.ShouldHavePageSize(pageSize);
        result.ShouldHaveTotalCount(entityCount);
        result.ShouldHaveItems(pageSize);
        elapsedMs.Should().BeLessThan(3000, "Paged query should complete within 3 seconds");
    }

    [Fact]
    public async Task Sum_LargeDataset_ShouldCompleteInReasonableTime()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 1000;
        var products = TestDataBuilder.CreateTestProducts(entityCount);
        await _productRepository.AddRange(products);

        var specification = SpecificationExtensions.Where<TestProduct>(p => p.IsActive);
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await _productRepository.Sum(specification, p => p.Price);

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        _output.WriteLine($"Sum operation on {entityCount} products took {elapsedMs}ms");
        
        result.Should().BeGreaterThan(0);
        elapsedMs.Should().BeLessThan(2000, "Sum operation should complete within 2 seconds");
    }

    #endregion

    #region Memory Management Tests

    [Fact]
    public async Task Repository_MultipleOperations_ShouldNotLeakMemory()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int iterations = 100;
        const int entitiesPerIteration = 50;
        
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Perform multiple cycles of operations
        for (int i = 0; i < iterations; i++)
        {
            var users = TestDataBuilder.CreateTestUsers(entitiesPerIteration);
            await _userRepository.AddRange(users);
            
            var retrievedUsers = await _userRepository.GetAll();
            retrievedUsers.Should().HaveCount((i + 1) * entitiesPerIteration);
            
            // Clean database every 10 iterations to prevent excessive memory usage
            if (i % 10 == 9)
            {
                await _fixture.CleanDatabase();
            }
        }

        // Force garbage collection and measure memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;
        
        _output.WriteLine($"Memory increase after {iterations} operations: {memoryIncrease / 1024}KB");

        // Assert
        // Memory increase should be reasonable (less than 50MB for this test)
        memoryIncrease.Should().BeLessThan(50 * 1024 * 1024, "Memory increase should be reasonable");
    }

    [Fact]
    public async Task Repository_EntityDetachment_ShouldWork()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 500;
        var users = TestDataBuilder.CreateTestUsers(entityCount);

        // Act
        var addedUsers = await _userRepository.AddRange(users);
        
        // Modify entities after they've been detached
        foreach (var user in addedUsers)
        {
            user.Age += 1; // Modify after detachment
        }

        // Update all entities - should work due to detachment
        var updateTasks = addedUsers.Select(async user => await _userRepository.Update(user));
        await Task.WhenAll(updateTasks);

        // Assert
        var retrievedUsers = await _userRepository.GetAll();
        retrievedUsers.Should().HaveCount(entityCount);
        
        // Verify updates were applied
        var originalAges = users.Select(u => u.Age).ToList();
        var updatedAges = retrievedUsers.Select(u => u.Age).ToList();
        
        foreach (var updatedAge in updatedAges)
        {
            originalAges.Should().Contain(updatedAge - 1, "Age should have been incremented by 1");
        }
    }

    [Fact]
    public async Task Repository_LargeResultSet_ShouldHandleMemoryEfficiently()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 5000;
        var users = TestDataBuilder.CreateTestUsers(entityCount);
        await _userRepository.AddRange(users);

        var initialMemory = GC.GetTotalMemory(true);

        // Act - Retrieve large result set
        var stopwatch = Stopwatch.StartNew();
        var result = await _userRepository.GetAll();
        stopwatch.Stop();

        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = finalMemory - initialMemory;

        _output.WriteLine($"Retrieved {entityCount} entities in {stopwatch.ElapsedMilliseconds}ms using {memoryUsed / 1024}KB");

        // Assert
        result.Should().HaveCount(entityCount);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000, "Large result set retrieval should complete within 10 seconds");
        
        // Memory usage should be reasonable (each entity is small, so total should be < 10MB)
        memoryUsed.Should().BeLessThan(10 * 1024 * 1024, "Memory usage should be reasonable for large result set");
    }

    [Fact]
    public async Task Repository_ConcurrentOperations_ShouldBeThreadSafe()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int concurrentTasks = 10;
        const int entitiesPerTask = 100;

        // Act - Run concurrent operations
        var tasks = new List<Task>();
        
        for (int i = 0; i < concurrentTasks; i++)
        {
            var taskIndex = i; // Capture loop variable
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _fixture.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TestUser>>();
                
                var users = TestDataBuilder.CreateTestUsers(entitiesPerTask);
                // Make names unique per task to avoid conflicts
                foreach (var user in users)
                {
                    user.Name = $"Task{taskIndex}_{user.Name}";
                }
                
                await repository.AddRange(users);
                
                var retrievedUsers = await repository.Query(
                    SpecificationExtensions.Where<TestUser>(u => u.Name.StartsWith($"Task{taskIndex}_"))
                );
                
                retrievedUsers.Should().HaveCount(entitiesPerTask);
            }));
        }

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        _output.WriteLine($"Completed {concurrentTasks} concurrent operations in {stopwatch.ElapsedMilliseconds}ms");

        // Assert
        var allUsers = await _userRepository.GetAll();
        allUsers.Should().HaveCount(concurrentTasks * entitiesPerTask);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000, "Concurrent operations should complete within 30 seconds");
    }

    #endregion

    #region Performance Benchmark Tests

    [Fact]
    public async Task Repository_CrudOperations_PerformanceBenchmark()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 1000;
        var users = TestDataBuilder.CreateTestUsers(entityCount);

        var stopwatch = new Stopwatch();

        // Benchmark Add operations
        stopwatch.Restart();
        await _userRepository.AddRange(users);
        var addTime = stopwatch.ElapsedMilliseconds;

        // Benchmark Read operations
        stopwatch.Restart();
        var allUsers = await _userRepository.GetAll();
        var readTime = stopwatch.ElapsedMilliseconds;

        // Benchmark Update operations (update first 100)
        var usersToUpdate = allUsers.Take(100).ToList();
        foreach (var user in usersToUpdate)
        {
            user.Age += 1;
        }
        
        stopwatch.Restart();
        foreach (var user in usersToUpdate)
        {
            await _userRepository.Update(user);
        }
        var updateTime = stopwatch.ElapsedMilliseconds;

        // Benchmark Delete operations (delete first 50)
        var usersToDelete = usersToUpdate.Take(50).ToList();
        stopwatch.Restart();
        foreach (var user in usersToDelete)
        {
            await _userRepository.Remove(user);
        }
        var deleteTime = stopwatch.ElapsedMilliseconds;

        // Log performance metrics
        _output.WriteLine($"Performance Benchmark for {entityCount} entities:");
        _output.WriteLine($"  Add: {addTime}ms ({(double)addTime / entityCount:F2}ms per entity)");
        _output.WriteLine($"  Read: {readTime}ms");
        _output.WriteLine($"  Update: {updateTime}ms for 100 entities ({(double)updateTime / 100:F2}ms per entity)");
        _output.WriteLine($"  Delete: {deleteTime}ms for 50 entities ({(double)deleteTime / 50:F2}ms per entity)");

        // Assert performance expectations
        addTime.Should().BeLessThan(10000, "Bulk add should complete within 10 seconds");
        readTime.Should().BeLessThan(2000, "Read all should complete within 2 seconds");
        updateTime.Should().BeLessThan(5000, "100 updates should complete within 5 seconds");
        deleteTime.Should().BeLessThan(3000, "50 deletes should complete within 3 seconds");
    }

    [Fact]
    public async Task Repository_ComplexQueries_PerformanceBenchmark()
    {
        // Arrange
        await _fixture.CleanDatabase();
        const int entityCount = 2000;
        var users = TestDataBuilder.CreateTestUsers(entityCount);
        await _userRepository.AddRange(users);

        var stopwatch = new Stopwatch();

        // Benchmark simple filtering
        stopwatch.Restart();
        var simpleFilter = await _userRepository.Query(
            SpecificationExtensions.Where<TestUser>(u => u.Age > 30)
        );
        var simpleFilterTime = stopwatch.ElapsedMilliseconds;

        // Benchmark complex filtering
        stopwatch.Restart();
        var complexFilter = await _userRepository.Query(
            SpecificationExtensions.Where<TestUser>(u => u.Age >= 25 && u.Age <= 45 && u.Name.Contains("User"))
        );
        var complexFilterTime = stopwatch.ElapsedMilliseconds;

        // Benchmark pagination
        stopwatch.Restart();
        var pagedResult = await _userRepository.FindPaged(
            SpecificationExtensions.Where<TestUser>(u => u.Age > 0), 5, 50
        );
        var paginationTime = stopwatch.ElapsedMilliseconds;

        // Benchmark aggregation
        stopwatch.Restart();
        var count = await _userRepository.Count(
            SpecificationExtensions.Where<TestUser>(u => u.Age >= 30)
        );
        var countTime = stopwatch.ElapsedMilliseconds;

        // Log performance metrics
        _output.WriteLine($"Query Performance Benchmark on {entityCount} entities:");
        _output.WriteLine($"  Simple filter: {simpleFilterTime}ms ({simpleFilter.Count()} results)");
        _output.WriteLine($"  Complex filter: {complexFilterTime}ms ({complexFilter.Count()} results)");
        _output.WriteLine($"  Pagination: {paginationTime}ms (page 5, size 50)");
        _output.WriteLine($"  Count: {countTime}ms (result: {count})");

        // Assert performance expectations
        simpleFilterTime.Should().BeLessThan(2000, "Simple filtering should complete within 2 seconds");
        complexFilterTime.Should().BeLessThan(3000, "Complex filtering should complete within 3 seconds");
        paginationTime.Should().BeLessThan(2000, "Pagination should complete within 2 seconds");
        countTime.Should().BeLessThan(1000, "Count operation should complete within 1 second");
    }

    #endregion
}
