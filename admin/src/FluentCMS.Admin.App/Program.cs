using FluentCMS.Admin;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AdminApp>("#app");
await builder.AddFluentCmsAdmin();
await builder.Build().RunAsync();
