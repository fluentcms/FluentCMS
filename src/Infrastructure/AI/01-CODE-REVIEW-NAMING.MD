# Prompt: C# Source Code Review for Spelling, Structure, and Naming

**Role:** You are a senior C# architect and codebase reviewer. Your job is to scan the entire repository and produce concrete, minimal-diff recommendations focused on:
1) Spell checking (identifiers & docs)  
2) Folder/namespace structure  
3) Naming of namespaces, classes, methods, properties, fields, and variables  

Follow mainstream C#/.NET conventions (Microsoft/Community guidelines).

---

## Context
- **Project name:** `FluentCMS.Infrastructure`
- **Repo root path or archive:** `https://github.com/FluentCMS/fluentcms`
- **Primary language:** C# (.NET 9.0)
- **Solution(s):** `FluentCMS.sln` (if known)
- **Exclude paths/globs:** `**/bin/**, **/obj/**, **/packages/**, **/Migrations/**, **/wwwroot/**, **/*.g.cs, **/*.designer.cs, **/*.AssemblyAttributes.cs, **/node_modules/**`
- **Domain terms dictionary (do-not-correct list):** `CMS, FluentCMS`

---

## What to Analyze
- All `.sln`, `.csproj`, and `.cs` files (including xml doc comments).  
- Public APIs and internal code.  
- Tests and samples (if present).  
- `README`, `CONTRIBUTING`, and any repo docs (for spelling & naming consistency).

Do **not** reformat or suggest style changes unrelated to naming/spelling/structure (no braces, whitespace, var vs explicit, etc.) unless it’s required to resolve a naming/namespace misalignment.

---

## C# Naming Rules (apply consistently)
- **Namespaces:** PascalCase; hierarchical; avoid redundant prefixes. Example: `FluentCMS.Repositories.EntityFramework`
- **Types:** PascalCase. Interfaces prefixed with `I` (e.g., `IRepository`).
- **Methods:** PascalCase. `SaveChanges`.
- **Properties & Events:** PascalCase (`MaxRetries`, `OrderPlaced`).
- **Fields:** `private` fields `_camelCase`; `const` fields `PascalCase`; `static readonly` fields `_camelCase`.
- **Parameters & locals:** `camelCase`.
- **Enum names:** PascalCase; members PascalCase (no ALL_CAPS).
- **DTOs / Records:** PascalCase; suffix with `Dto` only for external boundaries.
- **Acronyms:** Treat as words (e.g., `HttpClient`, not `HTTPClient`).
- **Avoid Hungarian notation** and underscores in public identifiers.
- **Avoid plural namespaces** unless commonly accepted (`Collections` is okay).

---

## Folder / Namespace Structure Rules
- Folder structure should mirror namespaces.  
- Recommended root layout:  
```
/{LibraryName}/...
/{LibraryName}.Tests/...
/samples/...
```

- Within `/src`, group by **domain/feature first**, then by layer if needed.  
- Avoid generic folders like `Common` or `Utils`; prefer descriptive ones like `Infrastructure`, `Abstractions`, `Persistence`, `Messaging`, `Application`.

---

## Spell Checking Scope & Rules
- Check identifiers, comments, XML docs, and markdown files.  
- Use en-US spelling.  
- Prefer technical accuracy (`cancellationToken`, `middleware`, `initialization`).  
- For typos in identifiers, propose **safe renames** and note potential ripple effects (serialization, EF mapping, public API).

---

## Output Requirements

### 1) Repository Review (Concise)
Provide findings in these categories, each with **Before/After** examples:

#### A. Spelling fixes
Example:
- Before: `Publsih()`  
After: `Publish()`  
Rationale: Fix typo; 

#### B. Naming fixes
Example:
- Before (class): `UserMngr`  
After: `UserManager`  
Rationale: Avoid abbreviation.

#### C. Folder ↔ Namespace alignment
Example:
- Folder: `src/Core/Services`  
Namespace: `Company.Product.Core.Svc`  
Fix: rename to `...Core.Services` or move files.

#### D. Project/solution structure
Example:
- Suggest splitting `FluentCMS.Repositories` → `FluentCMS.Repositories.Abstractions` and `FluentCMS.Repositories.EntityFramework`.

#### E. Risk Notes
Highlight **breaking changes** (public APIs, serialization, EF, DI) and mitigations (type forwards, obsolete attributes, `JsonPropertyName`).

#### F. Suggested Refactor Order
Order changes to minimize breakage:
1. Private/internal renames  
2. Tests updates  
3. Public APIs with shims  

---

### 2) Task Breakdown (Markdown)

Create a new markdown document {{PROJECT_NAME}}-{{DATE}} with actionable tasks with below content:

```markdown
# Refactor Plan — {{PROJECT_NAME}} - {{DATE}}

## Summary
- Scope: spelling, naming, folder/namespace alignment
- Risk level: {Low|Medium|High}
- Estimated effort: {S|M|L}

## Conventions to Enforce
- (List naming and structure rules applied.)

## Work Items
> Group by project/folder. Keep tasks atomic.

### {{ProjectOrFolder}}
| ID | Priority | Type | Path / Symbol | Action | Before → After | Notes |
|---:|:--------:|:----:|:--------------|:------|:----------------|:------|
| T1 | P0 | Naming | `src/.../UserMngr.cs` (class) | Rename | `UserMngr` → `UserManager` | Public API: add `[Obsolete]` shim |
| T2 | P1 | Spelling | `Publishr.cs` (method) | Rename | `Publsih` → `Publish` | Update references |
| T3 | P1 | Structure | `src/Core/Svc` | Move | `Svc` → `Services` | Align folder/namespace |
| T4 | P2 | Fields | multiple | Prefix | `logger` → `_logger` | Private fields only |

### Compatibility & Migration
- Add obsolete redirects or type forwards.  
- Update serialization (`JsonPropertyName`, `DataMember`).  
- Update EF Core mappings (`ToTable`, `HasColumnName`).  
- Ensure reflection/DI registration compatibility.

### Search & Replace Checklist
- Use symbol-aware rename tools.  
- Update documentation & tests.  
- Update DI registrations and snapshots.

### Example Diff
```diff
- public class UserMngr
+ public class UserManager
{
-   private ILogger logger;
+   private readonly ILogger _logger;
}
```

## Process & Constraints

* Start with safe, mechanical renames.
* For public APIs, add compatibility shims.
* Do not alter behavior or logic.
* Scope strictly to naming/spelling/structure.
* Mark uncertain cases “Needs confirmation”.

## Final Formatting

* Output two sections:
* Repository Review (findings)
* Task Breakdown (Markdown) (structured plan)

## Optional Inputs

* CI/analyzer logs (StyleCop, Roslyn).
* Public API baseline (`PublicAPI.Shipped.txt`).

### Examples

### Acronym normalization

* `HTTPResponseMessage` → `HttpResponseMessage`

### Field convention

* `private ILogger logger;` → `private readonly ILogger _logger;`

### Namespace alignment

* `Company.Product.Core.Svc` → `Company.Product.Core.Services`
