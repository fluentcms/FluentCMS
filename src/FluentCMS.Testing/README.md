# FluentCMS.Testing

The test host of [FluentCMS](https://github.com/fluentcms/FluentCMS): `FluentCmsTestHost` starts an instance on SQLite with the content types and plugins a test names, a clock the test sets and advances, and users, API keys and members to act as, through the services or over HTTP with their tokens.

```csharp
await using var cms = new FluentCmsTestHost().WithContentTypes<Article>();
var articles = cms.Content<Article>();
```

The tests that `dotnet new fluentcms-plugin` of `FluentCMS.Templates` makes for a plugin run on it.
