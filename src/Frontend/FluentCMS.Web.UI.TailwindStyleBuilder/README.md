# Tailwind Style Builder Component for Blazor

TailwindStyleBuilder is a utility for Blazor applications that integrates with Tailwind CSS via CDN to generate dynamic CSS classes. It allows developers to dynamically construct styles for their Blazor components, making it ideal for projects with rich, interactive, and dynamic content. The component provides a seamless way to apply Tailwind CSS styling without requiring a full build pipeline. By eliminating the need for a Node.js-based build system, it streamlines the integration of Tailwind CSS into Blazor projects, ensuring efficient and up-to-date design implementation.

## Features

- **Dynamic Style Building**: Generate CSS dynamically by specifying Tailwind CSS classes.
- **Tailwind CDN Integration**: Uses the Tailwind CSS CDN to ensure up-to-date styling.
- **Blazor Compatibility**: Designed specifically for Blazor-based projects.
- **Support for Dynamic Content**: Ideal for projects that render content dynamically in Blazor and need corresponding Tailwind CSS styling.

## Why TailwindStyleBuilder?
At [FluentCMS](https://github.com/FluentCMS/FluentCMS), we leverage Tailwind CSS to build our UIs. Since page content is dynamic and fetched from the database, it requires efficient handling of styles. In our initial approach, we considered building styles on the server-side using Node.js. However, this method proved to be resource-intensive and did not perform well.

We also explored using the Tailwind CDN for runtime styling. While this can work in some cases, generating styles on the fly during each request negatively impacts page load times and overall performance.

This is where TailwindStyleBuilder comes in. With TailwindStyleBuilder, we optimize style generation by building the CSS only once when the page content is first updated or visited by an admin. On subsequent visits, we serve the pre-generated CSS file, ensuring fast and efficient page rendering without the overhead of runtime styling.

By adopting TailwindStyleBuilder, we improve both performance and resource usage, making it an ideal solution for FluentCMS’s dynamic page content.

## Use Cases

- Dynamically styled components in Blazor.
- Applications generated base where CSS classes ared on runtime data.
- Projects using Tailwind CSS with Blazor, needing lightweight integration without a full build pipeline.

## Installation

To add **TailwindStyleBuilder** to your Blazor project, follow these steps:

1. Install the package via NuGet:

   ```bash
   dotnet add package FluentCMS.Web.UI.TailwindStyleBuilder
   ```

2. Import the namespace in your Blazor components or pages:

   ```csharp
   @using FluentCMS.Web.UI.TailwindStyleBuilder
   ```


## Usage

### Basic Example

Here’s a simple example of how to use TailwindStyleBuilder to generate styles dynamically:


Use the component in Head section of your App.razor file:

```csharp
<!DOCTYPE html>
<html lang="en">

<head>
    ...
    <TailwindStyleBuilder />
</head>

<body>
    ...
    <div class="bg-blue-200 text-blue-900 text-4xl">
      Hello World!
    </div>
</body>

</html>

```

now you are able to use Tailwind in your components.

### Advanced Example

While it functions similarly to using pure Tailwind CDN, our component offers an additional feature: the ability to generate CSS code dynamically and write it to the `wwwroot` directory. This allows you to use the generated CSS file directly, reducing runtime dependencies on the CDN and improving performance.


To utilize the dynamic CSS generation feature, you can build an interactive component that acts as a wrapper and writes the generated CSS to a file: 

```csharp
@rendermode RenderMode.InteractiveServer
@using FluentCMS.Web.UI.TailwindStyleBuilder

@if (System.IO.File.Exists($"wwwroot/css/{Name}.css"))
{
    <link rel="stylesheet" href=@($"/css/{Name}.css")>
}
else
{
    @* You can pass tailwind config using Config property *@
    <TailwindStyleBuilder OnCssGenerated="OnCssGenerated" />
}

@code {

  [Inject] IWebHostEnvironment Environment { get; set; } = default!;

  [Parameter]
  public string Name { get; set; } = "generated";
  
  public void OnCssGenerated(string css)
  {
      var fileName = Name + ".css";
      var filePath = Path.Combine(Environment.WebRootPath, "css", fileName);
      Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
      File.WriteAllText(filePath, css);
  }
}
```
Here is full implementation of Wrapper component used in fluentCMS [TailwindStyleBuilderWrapper.razor](https://github.com/fluentcms/FluentCMS/blob/dev/src/Frontend/FluentCMS.Web.UI/Components/TailwindStyleBuilderWrapper.razor) and [TailwindStyleBuilderWrapper.razor.cs](https://github.com/fluentcms/FluentCMS/blob/dev/src/Frontend/FluentCMS.Web.UI/Components/TailwindStyleBuilderWrapper.razor.cs)

Now you should remove `<TailwindStyleBiulder />` from App.razor and use this component in your page like this:

```csharp
@page "/example"

<HeadContent>
    @* will use example.css if exists, otherwise it will build that file in first visit *@ 
    <TailwindStyles Name="example" />
</HeadContent>

<div class="p-4 bg-blue-100 text-blue-900">
    <h1>Hello Tailwind!</h1>
</div>

```

## Update Styles

to update the css files, you need to remove previously generated css file.\
in above example, when you update /example page,  you should remove /wwwroot/css/example.css file. 

## Benefits

- No need to pre-compile styles or configure a full Tailwind CSS build pipeline.
- Tailwind CSS updates are automatically applied via the CDN.
- Simplifies styling for Blazor components.

## Limitations

- CSS generation happens at runtime, which might impact performance for highly dynamic or complex styling requirements. (only first visit)

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests on the [GitHub repository](https://github.com/fluentcms/FluentCMS).

## License

This project is licensed under the [MIT License](LICENSE).

