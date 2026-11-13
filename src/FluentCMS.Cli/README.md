# FluentCMS.Cli

`fluentcms`, the command-line tool of [FluentCMS](https://github.com/fluentcms/FluentCMS). Inside a host's project it runs the host's own commands: `migrations add`, `migrate`, `schema apply`, `export` and `import`, `backup` and `restore`, `user create` and `apikey create`, `seed` and `doctor`. Against a running instance, with `--url` and a management key in `FLUENTCMS_TOKEN`, it generates typed clients with `generate typescript` and `generate csharp`, imports a site, manages keys, users and jobs, and is a stdio proxy to the instance's MCP endpoint with `mcp`.

A host made with `dotnet new fluentcms` pins the tool in `.config/dotnet-tools.json`, so `dotnet tool restore` installs it at the version of the host's packages:

```bash
dotnet tool restore
dotnet fluentcms migrations add Initial
dotnet fluentcms migrate
```

[Run with Docker](https://github.com/fluentcms/FluentCMS/blob/main/docs/docker.md#administer-an-instance) lists the commands, which the image runs too.
