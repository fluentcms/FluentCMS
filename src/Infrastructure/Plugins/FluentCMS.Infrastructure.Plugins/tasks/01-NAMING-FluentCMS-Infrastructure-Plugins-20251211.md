# Refactor Plan — FluentCMS.Infrastructure.Plugins - 2025-12-11

## Overall Context
After reviewing the entire FluentCMS.Infrastructure.Plugins codebase, the project demonstrates excellent adherence to C#/.NET naming conventions and organizational structure. The code is well-structured with appropriate folder/namespace alignment and clear, descriptive naming throughout. Only one minor field naming consistency issue was identified.

## Summary
- **Scope:** spelling, naming, folder/namespace alignment, naming clarity
- **Risk level:** Low
- **Estimated effort:** S (Small)

### Analysis Results

#### A. Spelling Fixes
✅ **No spelling issues found** - All identifiers, comments, and XML documentation use correct spelling.

#### B. Naming Fixes
**Found 1 issue:**
- **Issue:** Inconsistent field reference in `Loader/PluginLoader.cs`
- **Before:** `logger.LogError(ex, "Error finding loaded assembly {Assembly}", assemblyPath);`
- **After:** `_logger.LogError(ex, "Error finding loaded assembly {Assembly}", assemblyPath);`
- **Rationale:** Should use the private field `_logger` consistently instead of the parameter `logger`.

#### C. Naming Enhancements (Clarity Improvements)
✅ **No clarity improvements needed** - All names are already descriptive and follow Microsoft C# naming conventions:
- Classes: PascalCase (e.g., `PluginManager`, `PluginDiscovery`)
- Interfaces: `I` prefix (e.g., `IPluginStartup`, `IPluginLoader`)
- Methods: PascalCase (e.g., `ConfigureServices`, `LoadPluginTypes`)
- Private fields: `_camelCase` (e.g., `_logger`, `_pluginSystemOptions`)
- Properties: PascalCase (e.g., `IgnoreErrors`, `ScanAssemblyPatterns`)

#### D. Folder ↔ Namespace Alignment
✅ **Perfect alignment** - All folders mirror their corresponding namespaces:
- `/` → `FluentCMS.Infrastructure.Plugins`
- `/Discovery/` → `FluentCMS.Infrastructure.Plugins.Discovery`
- `/Initializer/` → `FluentCMS.Infrastructure.Plugins.Initializer`
- `/Loader/` → `FluentCMS.Infrastructure.Plugins.Loader`

#### E. Project/Solution Structure
✅ **Well-organized structure** following .NET conventions:
- Clear separation of concerns with domain-specific folders
- Appropriate use of internal interfaces and implementations
- Consistent exception handling patterns
- Proper dependency injection setup

#### F. Risk Notes
- **Low Risk:** The single fix is an internal change that doesn't affect public APIs, serialization, or external contracts.
- **No Breaking Changes:** All changes are internal implementation details.

#### G. Suggested Refactor Order
1. Fix field naming consistency (internal change only)

### Tasks

|Status |    ID    | Priority | Type | Path / Symbol | Action | Before → After | Notes |
|-------|---------:|:--------:|:----:|:--------------|:------|:----------------|:------|
|&#9745;| T1 | P0 | Naming | `Loader/PluginLoader.cs` (line ~88) | Fix field reference | `logger.LogError` → `_logger.LogError` | Consistency with field naming convention |

## Conventions Enforced
✅ All Microsoft C# naming conventions are already properly followed:
- PascalCase for public types, methods, and properties
- `_camelCase` for private fields
- `I` prefix for interfaces
- Descriptive names avoiding abbreviations
- Proper folder/namespace alignment
- Clear separation of concerns

## Conclusion
This codebase is exemplary in terms of naming conventions and structure. The single minor inconsistency identified is easily fixed and poses no risk to functionality or external contracts. The development team has done an excellent job following C#/.NET best practices.
