# FluentCMS.Generators

The source generator of [FluentCMS](https://github.com/fluentcms/FluentCMS): for each content type, component and block a project declares with the schema attributes, it writes a typed filter and sort builder, such as `ArticleFields.Title.Contains("x")`, so that a query names fields the compiler checks.

Reference it as an analyzer only, so it is not a dependency of what the project ships:

```xml
<PackageReference Include="FluentCMS.Generators" Version="..." PrivateAssets="all" />
```

A host made with `dotnet new fluentcms` references it already. It needs the compiler of the .NET 10 SDK, 10.0.100 or later. Its diagnostics are `FCMS0100` to `FCMS0119` ([analyzer rules](https://github.com/fluentcms/FluentCMS/blob/main/docs/analyzers.md)).
