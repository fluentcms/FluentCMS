# FluentCMS.Cli

`fluentcms`, the command-line tool of [FluentCMS](https://github.com/fluentcms/FluentCMS). Inside a host's project it runs the host's own commands: `migrations add`, `migrate`, `schema apply`, `export` and `import`, `backup` and `restore`, `user create` and `apikey create`, `seed` and `doctor`. Against a running instance, with `--url` and a management key in `FLUENTCMS_TOKEN`, it generates typed clients with `generate typescript` and `generate csharp`, imports a site, manages keys, users and jobs, and is a stdio proxy to the instance's MCP endpoint with `mcp`.

A host made with `dotnet new fluentcms` pins the tool in `.config/dotnet-tools.json`, so `dotnet tool restore` installs it at the version of the host's packages:

```bash
dotnet tool restore
dotnet fluentcms migrations add Initial
dotnet fluentcms migrate
```

`generate csharp --namespace <name> --output <folder>` writes a file for each class into the folder and removes the files it wrote before and no longer generates. A folder holds the code of one namespace: a run with another `--namespace` neither removes nor writes over the files of the first, and is refused with `FCMS3381` when it would write one of them. Generate each namespace into a folder of its own, and to change the namespace of a folder, remove its generated files first. Two classes whose names differ in case alone would be one file where the file system ignores case, so the second type gets no class (`FCMS3376`) and no builder (`FCMS0100`), and files whose names differ in case alone are never written (`FCMS3380`).

[Run with Docker](https://github.com/fluentcms/FluentCMS/blob/main/docs/docker.md#administer-an-instance) lists the commands, which the image runs too.
