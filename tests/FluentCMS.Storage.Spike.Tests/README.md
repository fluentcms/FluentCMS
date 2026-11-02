# Storage spike (throwaway)

This project is the spike for [#2681](https://github.com/fluentcms/FluentCMS/issues/2681). It is not product code. Delete it once [#2688](https://github.com/fluentcms/FluentCMS/issues/2688) lands the real content tables.

The tests are the evidence for [ADR 0001](../../docs/decisions/0001-storage-spike-outcome.md). They build an EF Core model from a small runtime schema using shared-type (property-bag) entity types, and run it on SQLite. PostgreSQL and SQL Server are out of scope here and deferred to [#2711](https://github.com/fluentcms/FluentCMS/issues/2711).

To try the CLI path by hand, run this from the repository root, then delete the `Migrations` folder it creates in this project. A snapshot left there makes the in-process migration tests start from the wrong model, and the build fails until the generated migration is marked as generated (see the ADR).

```bash
dotnet ef migrations add Initial --project tests/FluentCMS.Storage.Spike.Tests
```
