# Plugin Pull Request Checklist

This checklist must be completed for any pull request that creates or modifies a plugin. Its purpose is to maintain system stability by catching common issues that can arise in a trusted, shared-process architecture.

## General
- [ ] **Single Responsibility**: Does the plugin adhere to a single, well-defined purpose?
- [ ] **Documentation**: Is the code clear and have inline comments been added for complex logic?
- [ ] **Configuration**: Are all configurable values exposed via the Options pattern and documented?
- [ ] **Event Catalog**: Has the `docs/EVENT_CATALOG.md` been updated for any new or modified events this plugin publishes?

## Stability & Performance
- [ ] **No Blocking Calls**: The code contains no synchronous blocking on `async` operations (e.g., no `.Result` or `.Wait()`).
- [ ] **Resource Management**: All `IDisposable` objects (e.g., `HttpClient`, `Stream`, `DbContext`) are properly managed with `using` statements or dependency injection lifetimes.
- [ ] **Graceful Error Handling**: The plugin handles potential exceptions gracefully. External API calls have appropriate retry/circuit-breaker logic if necessary.
- [ ] **CancellationToken Propagation**: All `async` methods accept a `CancellationToken` and pass it down to all subsequent async calls (database, HTTP, etc.).

## Security & Observability
- [ ] **Input Validation**: All external input (from APIs, events, etc.) is properly validated.
- [ ] **Structured Logging**: All logs are structured and include relevant context (e.g., `CorrelationId`, `PluginName`). No sensitive data (PII, secrets) is logged in plain text.

## Testing
- [ ] **Unit Tests**: Core business logic is covered by unit tests.
- [ ] **Integration Tests**: Key integration points (e.g., event handlers, API endpoints) are covered.
- [ ] **Happy & Sad Paths**: Tests cover both successful execution and expected failure modes.
