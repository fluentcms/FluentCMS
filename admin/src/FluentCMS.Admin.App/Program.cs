using FluentCMS.Admin;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<AdminApp>("#app");
builder.RootComponents.Add<FluentCMS.Admin.App.ComponentGallery>("#gallery");
await builder.AddFluentCmsAdmin();
await builder.Build().RunAsync();
