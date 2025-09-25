# FluentCMS Repository Integration Tests - Task Breakdown

## Overview
This document outlines the comprehensive task breakdown for implementing SQLite in-memory integration tests for the FluentCMS repository infrastructure.

## Project Structure

```
FluentCMS.Repositories.Tests.Integration/
├── FluentCMS.Repositories.Tests.Integration.csproj
├── TestEntities/
│   ├── TestUser.cs
│   ├── TestProduct.cs
│   └── TestCategory.cs
├── TestFixtures/
│   ├── SqliteTestFixture.cs
│   └── TestDbContext.cs
├── RepositoryTests/
│   ├── BasicCrudOperationsTests.cs
│   ├── SpecificationQueryTests.cs
│   └── ErrorHandlingTests.cs
├── PerformanceTests/
│   └── LargeDatasetTests.cs
└── Helpers/
    ├── TestDataBuilder.cs
    └── AssertionExtensions.cs
```

## Test Categories & Implementation Tasks

### Phase 1: Foundation & Infrastructure (Priority: High)

#### Task 1.1: Project Setup
- [x] Create test project with required NuGet packages
  - `Microsoft.EntityFrameworkCore.Sqlite`
  - `Microsoft.EntityFrameworkCore.InMemory`
  - `xunit`
  - `xunit.runner.visualstudio`
  - `FluentAssertions`
  - `Microsoft.Extensions.DependencyInjection`
  - `Microsoft.Extensions.Logging.Abstractions`
- [x] Configure project references to repository projects
- [x] Set up test project configuration

#### Task 1.2: Test Entities Creation
- [x] **TestUser.cs** - Basic entity with string properties
  ```csharp
  public class TestUser : IEntity
  {
      public Guid Id { get; set; }
      public string Name { get; set; }
      public string Email { get; set; }
      public int Age { get; set; }
  }
  ```

- [x] **TestProduct.cs** - Entity with decimal properties for Sum operations
  ```csharp
  public class TestProduct : IEntity
  {
      public Guid Id { get; set; }
      public string Name { get; set; }
      public decimal Price { get; set; }
      public int CategoryId { get; set; }
      public bool IsActive { get; set; }
  }
  ```

- [x] **TestCategory.cs** - Entity for relationship and grouping tests
  ```csharp
  public class TestCategory : IEntity
  {
      public Guid Id { get; set; }
      public string Name { get; set; }
      public string Description { get; set; }
  }
  ```

#### Task 1.3: Test Infrastructure
- [x] **SqliteTestFixture.cs** - Base test fixture with DI container setup
- [x] **TestDbContext.cs** - DbContext with test entities
- [x] **TestDataBuilder.cs** - Factory methods for creating test data
- [x] **AssertionExtensions.cs** - Custom FluentAssertions extensions

### Phase 2: Basic CRUD Operations (Priority: High)

#### Task 2.1: BasicCrudOperationsTests.cs
**Add Operations (8 tests)**
- [x] `Add_ValidEntity_ShouldGenerateIdAndReturnEntity`
- [x] `Add_EntityWithEmptyId_ShouldGenerateNewId`
- [x] `Add_EntityWithExistingId_ShouldKeepExistingId`
- [x] `Add_NullEntity_ShouldThrowArgumentNullException`
- [x] `AddRange_ValidEntities_ShouldAddAllAndReturnList`
- [x] `AddRange_EmptyList_ShouldReturnEmptyList`
- [x] `AddRange_NullList_ShouldThrowArgumentNullException`
- [x] `AddRange_EntitiesWithMixedIds_ShouldGenerateIdsForEmptyOnes`

**Update Operations (4 tests)**
- [x] `Update_ExistingEntity_ShouldUpdateAndReturnEntity`
- [x] `Update_NonExistingEntity_ShouldStillUpdate` (EF behavior)
- [x] `Update_NullEntity_ShouldThrowArgumentNullException`
- [x] `Update_EntityAfterDetachment_ShouldWork`

**Remove Operations (6 tests)**
- [x] `Remove_ExistingEntityByObject_ShouldRemoveAndReturnEntity`
- [x] `Remove_ExistingEntityById_ShouldRemoveAndReturnEntity`
- [x] `Remove_NonExistingEntityById_ShouldReturnNull`
- [x] `Remove_NonExistingEntityByObject_ShouldReturnNull`
- [x] `Remove_NullEntity_ShouldThrowArgumentNullException`
- [x] `Remove_EmptyGuid_ShouldReturnNull`

**Get Operations (4 tests)**
- [x] `GetById_ExistingEntity_ShouldReturnEntity`
- [x] `GetById_NonExistingEntity_ShouldReturnNull`
- [x] `GetById_EmptyGuid_ShouldReturnNull`
- [x] `GetAll_WithData_ShouldReturnAllEntities`

### Phase 3: Specification Query Operations (Priority: High)

#### Task 3.1: SpecificationQueryTests.cs
**Basic Query Operations (12 tests)**
- [x] `Query_WithWhereSpecification_ShouldReturnFilteredResults`
- [x] `Query_WithMultipleWhereConditions_ShouldApplyAllFilters`
- [x] `Query_WithNoMatches_ShouldReturnEmptyList`
- [x] `FirstOrDefault_WithMatches_ShouldReturnFirstEntity`
- [x] `FirstOrDefault_WithNoMatches_ShouldReturnNull`
- [x] `SingleOrDefault_WithSingleMatch_ShouldReturnEntity`
- [x] `SingleOrDefault_WithNoMatches_ShouldReturnNull`
- [x] `SingleOrDefault_WithMultipleMatches_ShouldThrowException`
- [x] `Count_WithFilters_ShouldReturnCorrectCount`
- [x] `Count_WithNoMatches_ShouldReturnZero`
- [x] `Any_WithMatches_ShouldReturnTrue`
- [x] `Any_WithNoMatches_ShouldReturnFalse`

**Ordering Operations (8 tests)**
- [x] `Query_WithOrderBy_ShouldReturnOrderedResults`
- [x] `Query_WithOrderByDescending_ShouldReturnDescendingResults`
- [x] `Query_WithOrderByThenBy_ShouldApplySecondarySort`
- [x] `Query_WithOrderByDescendingThenByDescending_ShouldApplyBothDescending`
- [x] `Query_WithComplexOrdering_ShouldMaintainSortOrder`
- [x] `Query_WithOrderByOnDifferentDataTypes_ShouldWork`
- [x] `Query_WithNullValues_ShouldHandleOrderingCorrectly`
- [x] `Query_WithOrderByOnNavigationProperty_ShouldWork`

**Pagination Operations (8 tests)**
- [x] `Query_WithSkip_ShouldSkipCorrectNumber`
- [x] `Query_WithTake_ShouldTakeCorrectNumber`
- [x] `Query_WithSkipAndTake_ShouldImplementPagination`
- [x] `Query_WithSkipGreaterThanTotal_ShouldReturnEmpty`
- [x] `Query_WithTakeGreaterThanRemaining_ShouldReturnAvailable`
- [x] `FindPaged_WithValidParams_ShouldReturnPagedResult`
- [x] `FindPaged_WithPageBeyondResults_ShouldReturnEmptyPage`
- [x] `FindPaged_WithLargePageSize_ShouldReturnAllResults`

**Aggregation Operations (6 tests)**
- [x] `Sum_WithValidSelector_ShouldReturnCorrectSum`
- [x] `Sum_WithNoMatches_ShouldReturnZero`
- [x] `Sum_WithNullValues_ShouldIgnoreNulls`
- [x] `Count_WithLargeDataset_ShouldReturnCorrectCount`
- [x] `Any_WithComplexPredicate_ShouldWork`
- [x] `Query_WithGroupBy_ShouldGroupCorrectly`

**Advanced Query Operations (6 tests)**
- [x] `Query_WithDistinct_ShouldRemoveDuplicates`
- [x] `Query_WithComplexSpecification_ShouldApplyAllOperations`
- [x] `Query_WithNestedExpressions_ShouldWork`
- [x] `Query_WithDateTimeComparisons_ShouldWork`
- [x] `Query_WithStringOperations_ShouldWork`
- [x] `Query_WithNullChecks_ShouldHandleNullsCorrectly`

### Phase 4: Error Handling & Edge Cases (Priority: Medium)

#### Task 4.1: ErrorHandlingTests.cs
**Parameter Validation (8 tests)**
- [x] `Add_NullEntity_ShouldThrowArgumentNullException`
- [x] `Update_NullEntity_ShouldThrowArgumentNullException`
- [x] `Remove_NullEntity_ShouldThrowArgumentNullException`
- [x] `Query_NullSpecification_ShouldThrowArgumentNullException`
- [x] `FirstOrDefault_NullSpecification_ShouldThrowArgumentNullException`
- [x] `SingleOrDefault_NullSpecification_ShouldThrowArgumentNullException`
- [x] `Count_NullSpecification_ShouldThrowArgumentNullException`
- [x] `Sum_NullSpecification_ShouldThrowArgumentNullException`

**Repository Exception Handling (6 tests)**
- [x] `Add_DatabaseError_ShouldThrowRepositoryException`
- [x] `Update_DatabaseError_ShouldThrowRepositoryException`
- [x] `Remove_DatabaseError_ShouldThrowRepositoryException`
- [x] `Query_InvalidSpecification_ShouldThrowRepositoryException`
- [x] `GetById_DatabaseError_ShouldThrowRepositoryException`
- [x] `GetAll_DatabaseError_ShouldThrowRepositoryException`

**Cancellation Token Handling (6 tests)**
- [x] `Add_CancelledToken_ShouldThrowOperationCancelledException`
- [x] `Update_CancelledToken_ShouldThrowOperationCancelledException`
- [x] `Remove_CancelledToken_ShouldThrowOperationCancelledException`
- [x] `Query_CancelledToken_ShouldThrowOperationCancelledException`
- [x] `GetById_CancelledToken_ShouldThrowOperationCancelledException`
- [x] `GetAll_CancelledToken_ShouldThrowOperationCancelledException`

### Phase 5: Performance & Large Dataset Tests (Priority: Low)

#### Task 5.1: LargeDatasetTests.cs
**Bulk Operations (6 tests)**
- [x] `AddRange_LargeDataset_ShouldCompleteInReasonableTime` (1000+ entities)
- [x] `GetAll_LargeDataset_ShouldCompleteInReasonableTime`
- [x] `Query_LargeDatasetWithFiltering_ShouldCompleteInReasonableTime`
- [x] `Count_LargeDataset_ShouldCompleteInReasonableTime`
- [x] `FindPaged_LargeDataset_ShouldCompleteInReasonableTime`
- [x] `Sum_LargeDataset_ShouldCompleteInReasonableTime`

**Memory Management (4 tests)**
- [x] `Repository_MultipleOperations_ShouldNotLeakMemory`
- [x] `Repository_EntityDetachment_ShouldWork`
- [x] `Repository_LargeResultSet_ShouldHandleMemoryEfficiently`
- [x] `Repository_ConcurrentOperations_ShouldBeThreadSafe`

## Implementation Timeline

### Week 1: Foundation
- [ ] Complete Phase 1 (Project setup and infrastructure)
- [ ] Implement test entities and fixtures
- [ ] Set up CI/CD integration

### Week 2: Core Functionality
- [ ] Complete Phase 2 (Basic CRUD operations - 22 tests)
- [ ] Begin Phase 3 (Basic query operations - 12 tests)

### Week 3: Advanced Queries
- [ ] Complete remaining Phase 3 (Ordering, pagination, aggregation - 28 tests)
- [ ] Begin Phase 4 (Error handling - 20 tests)

### Week 4: Quality & Performance
- [ ] Complete Phase 4 (Error handling)
- [ ] Complete Phase 5 (Performance tests - 10 tests)
- [ ] Code review and documentation

## Success Criteria

### Code Coverage
- [ ] Achieve 90%+ code coverage for Repository class
- [ ] Achieve 85%+ code coverage for Specification classes
- [ ] All public methods covered by integration tests

### Test Quality
- [ ] All tests follow AAA pattern (Arrange, Act, Assert)
- [ ] Clear, descriptive test names
- [ ] Comprehensive edge case coverage
- [ ] Performance benchmarks established

### Documentation
- [ ] All test categories documented
- [ ] Test data setup clearly explained
- [ ] Troubleshooting guide for test failures

## Dependencies & Tools

### NuGet Packages Required
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.0" />
<PackageReference Include="xunit" Version="2.4.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.4.5" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
```

### Project References
- `FluentCMS.Repositories.Abstractions`
- `FluentCMS.Repositories.EntityFramework`
- `FluentCMS.Repositories.SQLite`
- `FluentCMS.Shared`

## Final Test Count Summary
- **Phase 1**: Infrastructure setup (not counted)
- **Phase 2**: Basic CRUD Operations - 22 tests
- **Phase 3**: Specification Queries - 40 tests
- **Phase 4**: Error Handling - 20 tests
- **Phase 5**: Performance Tests - 10 tests

**Total: 92 Integration Tests**

## Notes
- All tests will use SQLite in-memory databases for isolation
- No audit trail integration testing required
- Focus on repository pattern functionality and specification queries
- Performance tests should establish baseline benchmarks
- All async operations must include proper CancellationToken testing
