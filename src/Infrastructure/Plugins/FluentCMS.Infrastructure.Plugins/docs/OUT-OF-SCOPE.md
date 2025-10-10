# Out of Scope Features

## Overview

This document details features and capabilities that were considered during the design of the FluentCMS Plugin System but were intentionally excluded from the current implementation. Each exclusion includes the rationale behind the decision and potential future considerations.

## Table of Contents

- [Hot-Reload and Dynamic Loading](#hot-reload-and-dynamic-loading)
- [Permission-Based Access Control](#permission-based-access-control)
- [Assembly Isolation](#assembly-isolation)
- [Sandboxing](#sandboxing)
- [Multi-Tenancy Support](#multi-tenancy-support)
- [Plugin Versioning and Migration](#plugin-versioning-and-migration)
- [Environment-Based Plugin Loading](#environment-based-plugin-loading)
- [Plugin Communication Tracing](#plugin-communication-tracing)
- [Plugin Marketplace](#plugin-marketplace)
- [Summary](#summary)

---

## Hot-Reload and Dynamic Loading

### What It Is

Hot-reload allows plugins to be added, removed, or updated without restarting the application. This would enable:
- Loading new plugins at runtime
- Unloading and replacing existing plugins
- Applying plugin updates without downtime

### Why It Was Considered

- **Developer Experience**: Faster development cycle without constant restarts
- **Production Flexibility**: Update plugins without application downtime
- **Dynamic Extensibility**: Add features to running applications

### Why It's Excluded

#### Technical Limitations

**1. .NET DI Container is Immutable**
```csharp
// Once built, the DI container cannot be modified
var serviceProvider = services.BuildServiceProvider();
// Cannot add new services after this point
```

**2. Rebuilding Container is Risky**
- Existing service instances would be invalidated
- In-flight requests would fail
- Shared state would be lost
- Thread safety issues

**3. Middleware Pipeline is Immutable**
```csharp
// Once Configure() is called, pipeline is fixed
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// Cannot insert new middleware after this
```

#### Alternative Considered: Factory Pattern

We explored a hybrid approach using factory patterns:

```csharp
// Instead of direct registration
services.AddScoped<IService, ServiceImpl>();

// Use factory wrapper
services.AddScoped<IService>(provider => 
    provider.GetRequiredService<PluginServiceFactory>()
        .CreateService());
```

**Problems:**
- Only works for scoped/transient services, not singletons
- Adds complexity and indirection
- Cannot swap middleware
- Performance overhead

### Rationale for Exclusion

1. **Compile-Time Safety**: Current approach ensures all dependencies are verified at build time
2. **Predictability**: Application behavior is consistent and deterministic
3. **Simplicity**: Avoiding complex hot-reload logic reduces bugs
4. **Production Reality**: Most production deployments use rolling updates anyway

### When to Reconsider

Consider adding if:
- .NET introduces first-class hot-reload support for DI
- Specific use case requires runtime plugin installation
- Team has resources to handle the complexity
- Zero-downtime plugin updates become critical

### Current Alternative

**Use fast restart mechanisms:**
- Docker container restarts (< 5 seconds)
- Kubernetes rolling updates
- Blue-green deployments

---

## Permission-Based Access Control

### What It Is

A system where plugins declare required permissions and the host enforces access control:

```csharp
[Plugin]
[RequiredPermissions(
    PluginPermissions.FileSystemAccess,
    PluginPermissions.DatabaseAccess,
    PluginPermissions.NetworkAccess)]
public class MyPlugin : IPluginStartup
{
    // Plugin code
}
```

The host would validate and enforce these permissions at runtime.

### Why It Was Considered

- **Security**: Limit plugin capabilities
- **Audit Trail**: Track what plugins can access
- **User Control**: Allow administrators to enable/disable permissions
- **Untrusted Plugins**: Run third-party plugins safely

### Why It's Excluded

#### Current Architecture Implications

**1. Plugins are Trusted Code**
- Plugins are compiled with the main application
- Direct project references (not loaded from external sources)
- Same trust boundary as host application

**2. API Gateway Pattern Provides Control**
```csharp
// Plugins access features through controlled interfaces
public class InvoiceService
{
    private readonly IEmailSender _emailSender;      // Host provides
    private readonly IFileStorage _fileStorage;      // Host provides
    
    // Plugin can only use what host explicitly provides
}
```

**3. No External Plugin Sources**
- Not loading plugins from marketplace
- Not loading plugins from user uploads
- All plugins are known at compile-time

#### Complexity vs. Benefit

**High Complexity:**
- Runtime permission checking at every operation
- Metadata management for permissions
- UI for permission management
- Audit logging infrastructure

**Low Benefit:**
- Plugins already have full application access
- Team controls all plugin code
- Code reviews provide security

### Rationale for Exclusion

1. **Trust Model**: Internal plugins are part of the application, not external code
2. **Simpler Alternative**: API Gateway pattern provides sufficient control
3. **Development Speed**: Team can iterate faster without permission bureaucracy
4. **Maintenance Burden**: Permission system would need constant updates

### When to Reconsider

Consider adding if:
- Opening platform to third-party plugin developers
- Loading plugins from external sources
- Regulatory compliance requires permission tracking
- Moving to plugin marketplace model

### Current Alternative

**API Gateway Pattern:**
- Plugins access host features through interfaces
- Host controls what's available
- Dependency injection manages access
- Code reviews ensure proper usage

---

## Assembly Isolation

### What It Is

Loading plugins in separate `AssemblyLoadContext` to isolate their dependencies:

```csharp
public class PluginLoadContext : AssemblyLoadContext
{
    public PluginLoadContext() : base(isCollectible: true)
    {
    }
}

// Load plugin in isolated context
var context = new PluginLoadContext();
var assembly = context.LoadFromAssemblyPath(pluginPath);
```

### Why It Was Considered

- **Dependency Conflicts**: Different plugins could use different versions of same library
- **Memory Management**: Unload plugins to free memory
- **Side-by-Side Versions**: Run multiple versions of same plugin
- **Isolation**: Plugin crashes don't affect host

### Why It's Excluded

#### Architectural Decision: Shared Context

**1. Plugins Are Compiled With Host**
```xml
<!-- Main application references plugins -->
<ItemGroup>
  <ProjectReference Include=".../FluentCMS.Plugins.CRM.csproj" />
  <ProjectReference Include=".../FluentCMS.Plugins.Accounting.csproj" />
</ItemGroup>
```

All assemblies load in same context at startup.

**2. Core Features Need Tight Integration**

Identity/Authentication plugin must:
- Integrate with ASP.NET Core auth pipeline
- Share user context across all plugins
- Access auth middleware

**Isolation would cause:**
- Serialization overhead for every auth check
- Complex cross-context communication
- Performance degradation

**3. Event Bus Requires Shared Types**
```csharp
// Publisher and subscriber must share event type
public class CustomerCreatedEvent : IEvent { }

// With isolation, would need:
// - Type serialization/deserialization
// - Contract versioning
// - Complex marshaling
```

#### Complexity Issues

**Problems with AssemblyLoadContext:**
- Complex lifetime management
- Debugging difficulties
- Serialization overhead
- Type compatibility issues
- Limited unloading scenarios

### Rationale for Exclusion

1. **Integration Over Isolation**: Plugins need tight integration with host
2. **Performance**: Shared context is significantly faster
3. **Simplicity**: Easier to develop, debug, and maintain
4. **Compile-Time Verification**: MSBuild ensures dependency compatibility

### When to Reconsider

Consider adding if:
- Loading untrusted third-party plugins
- Need true plugin unloading
- Dependency conflicts become common
- Plugins require different framework versions

### Current Alternative

**Compile-Time Dependency Management:**
- MSBuild resolves conflicts
- Build fails if incompatible
- All plugins use same framework version
- Clear error messages at build time

---

## Sandboxing

### What It Is

Running plugins in restricted environments with limited system access:

```csharp
// Hypothetical sandboxing
[Plugin]
[Sandbox(
    AllowFileSystem = false,
    AllowNetwork = true,
    AllowReflection = false,
    MemoryLimit = "100MB")]
public class SandboxedPlugin : IPluginStartup
{
}
```

### Why It Was Considered

- **Security**: Limit damage from malicious code
- **Resource Protection**: Prevent resource exhaustion
- **Fault Isolation**: Plugin failures don't crash host
- **Compliance**: Meet security requirements

### Why It's Excluded

#### Same Trust Boundary

**Plugins Are Trusted Code:**
- Developed by same team as host
- Code reviewed before deployment
- Compiled into same application
- Share same process and memory space

**No Untrusted Code Scenario:**
- Not loading user-submitted plugins
- Not running community plugins
- Not executing arbitrary code

#### .NET Limitations

**.NET Core/5+ Removed Code Access Security (CAS):**
- No built-in sandboxing in modern .NET
- Would need OS-level containers
- Complex implementation

**Alternative: Process Isolation**
Would require:
- Running each plugin in separate process
- IPC for communication
- Significant performance overhead
- Massive complexity

### Rationale for Exclusion

1. **Trust Model**: Plugins are first-class application components
2. **Platform Limitations**: .NET doesn't provide sandboxing
3. **Overkill**: Sandboxing is for untrusted code
4. **Performance**: Would significantly impact performance

### When to Reconsider

Consider adding if:
- Opening to third-party developers
- Regulatory requirements mandate isolation
- Executing user-submitted code
- Moving to containerized plugin model

### Current Alternative

**Code Reviews and Testing:**
- All plugins code reviewed
- Automated testing
- Security scanning
- Team maintains all plugin code

---

## Multi-Tenancy Support

### What It Is

Different tenants (customers) could have different sets of plugins:

```csharp
// Tenant A: CRM + Accounting
// Tenant B: CRM + Inventory
// Tenant C: All plugins

public interface IPluginSystem
{
    List<IPlugin> GetPluginsForTenant(string tenantId);
    void EnablePluginForTenant(string pluginName, string tenantId);
}
```

### Why It Was Considered

- **SaaS Applications**: Different customers need different features
- **License Management**: Enable plugins based on subscription
- **Resource Optimization**: Only load needed plugins
- **Customization**: Per-tenant feature sets

### Why It's Excluded

#### Architectural Complexity

**1. Plugin Loading is Application-Wide**
```csharp
// Current: All plugins loaded at startup
services.AddPluginSystem();

// Multi-tenancy would need:
// - Per-request plugin resolution
// - Conditional middleware
// - Complex service registration
```

**2. Shared Infrastructure**
- Plugins register services in global DI container
- Middleware pipeline is application-wide
- Cannot conditionally apply middleware per tenant

**3. Service Resolution Issues**
```csharp
// How would this work?
public class OrderController
{
    // Which tenant's accounting plugin?
    private readonly IAccountingService _accounting;
}
```

#### Current Use Case

**Application is Single-Tenant:**
- Deployed per customer
- Each deployment configured for that customer
- Plugins selected at deployment time

### Rationale for Exclusion

1. **Not Required**: Current use case is single-tenant deployments
2. **Significant Complexity**: Would require complete redesign
3. **Performance**: Per-request plugin resolution would be slow
4. **Maintenance**: Much harder to debug and test

### When to Reconsider

Consider adding if:
- Moving to multi-tenant SaaS model
- Need per-customer feature flags
- License-based feature enabling required
- Shared infrastructure becomes cost priority

### Current Alternative

**Deployment-Based Configuration:**
```json
// Customer A deployment
{
  "Plugins": {
    "ScanAssemblyPatterns": ["*.CRM.*", "*.Accounting.*"]
  }
}

// Customer B deployment
{
  "Plugins": {
    "ScanAssemblyPatterns": ["*.CRM.*", "*.Inventory.*"]
  }
}
```

---

## Plugin Versioning and Migration

### What It Is

Managing multiple versions of plugins and migrating data between versions:

```csharp
[Plugin]
[Version("2.0.0")]
[MigrationPath("1.0.0 -> 2.0.0", typeof(CRMMigration_1_to_2))]
public class CRMPlugin : IPluginStartup
{
}

public class CRMMigration_1_to_2 : IPluginMigration
{
    public async Task Migrate(MigrationContext context)
    {
        // Migrate data from v1 to v2
    }
}
```

### Why It Was Considered

- **Smooth Updates**: Automatic data migration
- **Backward Compatibility**: Support old and new simultaneously
- **Rollback**: Ability to downgrade
- **Complex Scenarios**: Multiple migration paths

### Why It's Excluded

#### Plugin Update Model

**Plugins Updated With Host:**
- Plugins are part of application codebase
- All updated together in deployment
- No separate plugin update cycle

**Database Migrations Separate:**
```csharp
// Each plugin manages own migrations via EF Core
public class CRMDbContext : DbContext
{
    // Standard EF Core migrations
}
```

**Standard migration tools work:**
```bash
dotnet ef migrations add UpdateCustomerSchema
dotnet ef database update
```

#### Complexity Not Justified

**Would Need:**
- Version detection mechanism
- Migration dependency graph
- Rollback support
- Version compatibility matrix
- Complex testing scenarios

**Benefits:**
- None, since plugins don't update independently

### Rationale for Exclusion

1. **Update Model**: Plugins deploy with host application
2. **Existing Tools**: EF Core migrations handle database changes
3. **Simplicity**: Standard deployment processes work
4. **Not Required**: No independent plugin versioning

### When to Reconsider

Consider adding if:
- Plugins update independently from host
- Need side-by-side version support
- Plugin marketplace with version management
- Complex upgrade paths required

### Current Alternative

**Standard Deployment Process:**
1. Update plugin code
2. Create EF Core migration if needed
3. Deploy new application version
4. Run database migrations
5. Restart application

---

## Environment-Based Plugin Loading

### What It Is

Loading different plugins based on environment (Development, Staging, Production):

```csharp
// appsettings.Development.json
{
  "Plugins": {
    "Enabled": ["*"],
    "Disabled": ["PaymentPlugin"]
  }
}

// appsettings.Production.json
{
  "Plugins": {
    "Enabled": ["CRM", "Accounting", "Payment"],
    "Disabled": ["DebugPlugin", "TestPlugin"]
  }
}
```

### Why It Was Considered

- **Development Speed**: Disable slow plugins in dev
- **Testing**: Enable test plugins only in dev
- **Safety**: Disable payment processing in dev/staging
- **Feature Flags**: Environment-based features

### Why It's Excluded (Currently)

**Marked as Future Enhancement:**

This feature is **not excluded permanently** but deferred to Phase 2:

#### Current Phase Focus

**Phase 1 Priority:**
- Core plugin system working
- Dependency resolution
- Lifecycle management
- Basic health checks

**Avoiding Scope Creep:**
- Get basic system working first
- Add advanced features later
- Validate core design before extensions

#### Technical Foundation Needed

Before implementing:
1. Core plugin loading must be stable
2. Configuration system must be proven
3. Testing infrastructure must be in place

### Rationale for Deferral

1. **Priority**: Core features first
2. **Workarounds Exist**: Can comment out plugins in dev
3. **Complexity**: Adds configuration complexity
4. **Testing**: Need to test all environment combinations

### Future Implementation Plan

**Phase 2 Feature:**

```csharp
public class PluginSystemOptions
{
    public string[] ScanAssemblyPatterns { get; set; }
    public bool IgnoreErrors { get; set; }
    
    // Phase 2: Add environment filtering
    public string[] EnabledPlugins { get; set; }
    public string[] DisabledPlugins { get; set; }
}
```

**Implementation Steps:**
1. Add configuration properties
2. Filter discovered plugins
3. Update documentation
4. Add tests for all environments

### Current Workaround

**Manual Configuration:**
```csharp
#if DEBUG
    options.ScanAssemblyPatterns = new[] { "*.CRM.*", "*.Debug.*" };
#else
    options.ScanAssemblyPatterns = new[] { "*.CRM.*", "*.Accounting.*" };
#endif
```

---

## Plugin Communication Tracing

### What It Is

Tracking and correlating events across plugin boundaries:

```csharp
// Automatic correlation
[Event]
public class CustomerCreatedEvent : IEvent
{
    public Guid EventId { get; set; }
    public Guid CorrelationId { get; set; }  // Auto-added
    public string SourcePlugin { get; set; }  // Auto-tracked
}

// Tracing UI
GET /api/plugins/trace/{correlationId}
// Shows: Plugin A -> Event -> Plugin B -> Event -> Plugin C
```

### Why It Was Considered

- **Debugging**: Understand cross-plugin workflows
- **Performance**: Identify slow event chains
- **Monitoring**: Track plugin interactions
- **Documentation**: Auto-generate interaction diagrams

### Why It's Excluded (Currently)

**Marked as Future Enhancement:**

Also deferred to Phase 2 for similar reasons:

#### Current Phase Focus

**Basic Event Bus Working:**
- Plugins can publish events
- Plugins can subscribe to events
- Standard logging captures events

**Tracing Adds Complexity:**
- Correlation ID management
- Storage for trace data
- Query API for traces
- UI for visualization

### Rationale for Deferral

1. **Core First**: Event bus must work before tracing
2. **Existing Tools**: Can use Application Insights/Seq
3. **Complexity**: Adds storage and query layer
4. **Priority**: Nice-to-have vs. essential

### Future Implementation Plan

**Phase 2 Feature:**

```csharp
public interface IEventPublisher
{
    Task Publish<TEvent>(
        TEvent data, 
        CancellationToken cancellationToken = default,
        TraceContext? traceContext = null)  // Phase 2
        where TEvent : class, IEvent;
}

public class TraceContext
{
    public Guid CorrelationId { get; set; }
    public string SourcePlugin { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
```

### Current Workaround

**Structured Logging:**
```csharp
_logger.LogInformation(
    "Publishing {EventType} from {PluginName}",
    typeof(TEvent).Name,
    "CRM Plugin");
```

**External Tools:**
- Application Insights
- Seq
- ELK Stack
- Correlation via EventId

---

## Plugin Marketplace

### What It Is

A centralized repository for discovering and installing plugins:

```csharp
// Install from marketplace
await pluginMarketplace.Install("FluentCMS.Plugins.Analytics");

// Browse available plugins
var plugins = await pluginMarketplace.Search("analytics");

// Update plugins
await pluginMarketplace.Update("FluentCMS.Plugins.CRM");
```

### Why It Was Considered

- **Discovery**: Find available plugins
- **Sharing**: Share plugins across teams
- **Updates**: Centralized plugin updates
- **Community**: Enable plugin ecosystem

### Why It's Excluded

#### Not in Current Business Model

**Internal Plugin System:**
- Plugins developed by internal team
- No third-party plugins
- No community contributions
- No need for distribution

**Deployment Model:**
- Plugins compiled with application
- No runtime installation
- Standard deployment process

#### Would Require

**Massive Infrastructure:**
1. Package repository (like NuGet)
2. Authentication and authorization
3. Plugin signing and verification
4. Version management
5. Security scanning
6. Billing/licensing (if commercial)
7. Support infrastructure
8. Documentation hosting

**Runtime Loading:**
- AssemblyLoadContext isolation
- Dependency resolution
- Security boundaries
- Hot-reload capability

### Rationale for Exclusion

1. **Business Model**: Internal plugins only
2. **Complexity**: Marketplace is a product itself
3. **Security**: Opens attack surface
4. **Maintenance**: Requires dedicated team
5. **Not Required**: Current model works

### When to Reconsider

Consider adding if:
- Pivoting to plugin platform business
- Opening to third-party developers
- Building plugin ecosystem
- Monetizing through plugin marketplace

### Current Alternative

**Source Code Management:**
- Plugins in same repository as host
- Internal NuGet feed for shared libraries
- Standard Git workflow
- Code reviews for all changes

---

## Summary

### Exclusion Categories

**1. Technical Limitations**
- Hot-reload: .NET DI constraints
- Assembly Isolation: Integration requirements

**2. Trust Model**
- Permission-Based Access: Plugins are trusted
- Sandboxing: Internal code only

**3. Use Case Mismatch**
- Multi-Tenancy: Single-tenant deployments
- Plugin Marketplace: Internal plugins only
- Versioning: Update with host

**4. Future Enhancements** (Not Excluded, Deferred)
- Environment-Based Loading: Phase 2
- Communication Tracing: Phase 2

### Decision Framework

When evaluating future features, consider:

**Include If:**
- ✅ Required for core functionality
- ✅ Aligns with trust model
- ✅ Justified by current use cases
- ✅ Manageable complexity
- ✅ Available in .NET

**Exclude If:**
- ❌ Platform limitations prevent it
- ❌ Trust model doesn't require it
- ❌ No current use case
- ❌ Complexity outweighs benefit
- ❌ Better alternatives exist

### Future Roadmap

**Phase 1 (Current):**
- Core plugin system
- Dependency resolution
- Lifecycle management
- Health checks

**Phase 2 (Future):**
- Environment-based loading
- Communication tracing
- Advanced monitoring
- Performance optimizations

**Phase 3 (If Needed):**
- Assembly isolation (if untrusted plugins)
- Permission system (if third-party plugins)
- Marketplace (if platform business)

### Maintaining This Document

This document should be updated when:
- New features are considered and rejected
- Requirements change
- Previously excluded features become needed
- Architecture evolves

**Last Updated:** October 10, 2025  
**Version:** 1.0.0

---

For questions about any excluded feature, refer to the [Architecture Guide](./ARCHITECTURE.md) or contact the development team.
