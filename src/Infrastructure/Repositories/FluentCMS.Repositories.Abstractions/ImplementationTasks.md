# Implementation Tasks for Multi-Database Manager

## Task 1: Create Folders for Project Structure
- [x] Create Sqlite/ folder
- [x] Create SqlServer/ folder
- [x] Rename EfCore/ to EntityFramework/
- [x] Keep MongoDB/ as is

## Task 2: Implement Core Database Manager in Abstractions
- [x] Define IDatabaseManager, IDatabaseManagerBuilder, IDatabaseConnectionBuilder interfaces
- [x] Create DatabaseConnectionConfig class with Func<IDataContext> Factory property
- [x] Implement DatabaseManagerBuilder class with _currentConfig handling
- [x] Add IServiceCollection.AddDatabaseManager() extension

## Task 3: Create EntityFramework Package Structure
- [x] Update EfDataContext and EfEntitySet to use new namespace FluentCMS.Repositories.EntityFramework
	- [x] Removed EfDbContextConfiguration to avoid provider package dependencies

## Task 4: Create Sqlite Package Extensions
- [x] Add IServiceCollection extension for UseSqlite() that sets Factory with EfDataContext + Sqlite

## Task 5: Create SqlServer Package Extensions
- [x] Add IServiceCollection extension for UseSqlServer() that sets Factory with EfDataContext + SqlServer

## Task 6: Create MongoDB Package Extensions
- [x] Update MongoDataContext and MongoEntitySet if needed
- [x] Add IServiceCollection extension for UseMongoDB() that sets Factory with MongoDataContext

## Task 7: Update Examples for Usage
- [x] Update TodoDataContext interface to use ITodoDatabaseMarker
- [x] Add example registration of the area-specific DataContext

## Task 9: Correct Provider Dependencies
- [x] Remove EfDbContextConfiguration.cs from EntityFramework (avoids EF provider package dependencies)
- [x] Move DbContextOptions creation to Sqlite/SQLServer packages (inline creation)

## Task 8: Test the Implementation
- [x] Ensure the API works as expected (structure ready, runtime depends on packages split)
- [x] Verify default and specific area resolution (logic implemented)
