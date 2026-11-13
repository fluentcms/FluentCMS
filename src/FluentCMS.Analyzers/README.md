# FluentCMS.Analyzers

The Roslyn analyzers of [FluentCMS](https://github.com/fluentcms/FluentCMS): they report in the editor and the build what schema validation would refuse when the host starts, for content types, components and blocks declared with the schema attributes.

Reference it as an analyzer only, so it is not a dependency of what the project ships:

```xml
<PackageReference Include="FluentCMS.Analyzers" Version="..." PrivateAssets="all" />
```

A host made with `dotnet new fluentcms` and a plugin made with `dotnet new fluentcms-plugin` reference it already. It needs the compiler of the .NET 10 SDK, 10.0.100 or later. Each rule is described in [analyzer rules](https://github.com/fluentcms/FluentCMS/blob/main/docs/analyzers.md).
