using FluentCMS.Host;

return args is ["mcp", ..]
    ? await FluentCmsHost.RunMcpAsync(args[1..])
    : await FluentCmsHost.RunWebAsync(args);
