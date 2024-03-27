# FluentCMS

FluentCMS is a modern Content Management System (CMS) built on the powerful ASP.NET framework and the innovative Blazor technology. Infused with cutting-edge AI capabilities, FluentCMS assists content writers in crafting content more efficiently. Designed to be fast, flexible, and user-friendly, it not only serves as a traditional content-based CMS but also excels as a headless CMS, making it perfect for a diverse range of digital applications.


## Features

- **AI-Powered Writing Assistance**: Utilize advanced AI to guide content creation, suggesting optimizations and improving readability.
- **Blazing Fast**: Built on top of Blazor components for client-side operations.
- **Extensible**: Easily extend with custom plugins and themes.
- **Responsive**: Mobile-friendly out of the box.
- **Modern UI**: Sleek and intuitive dashboard for content management.
- ... *(Add more features as applicable)*

## Getting Started

### Prerequisites

- .NET SDK 8.0 or higher
- MongoDb

### Installation

1. Clone the repository:
    ```bash
    git clone https://github.com/fluentcms/FluentCMS.git
    ```

2. Navigate to the project directory and restore the NuGet packages:
    ```bash
    cd FluentCMS
    dotnet restore
    ```

3. Run the application:
    ```bash
    dotnet run
    ```

4. Visit `http://localhost:5000` in your browser.

*(You can expand on more detailed setup instructions, configurations, etc.)*

## Running e2e Tests for FluentCMS APIs

To run the end-to-end tests for FluentCMS APIs, use the following command in the terminal:

```shell
dotnet test ./src/Tests/FluentCMS.E2eTests
```

- Please ensure that the project is running at `http://localhost:5000` you can change this endpoint by modifing `./src/tests/FluentCMS.E2eTests/appsettings.json`
  
- For a comprehensive understanding of the available options, you may wish to consult the official [dotnet-test](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test) documentation.
  

## Generating LivingDoc

To generate a [LivingDoc](https://specflow.org/tools/living-doc/) report, use the following command in the terminal:

```shell
livingdoc feature-folder ./src/Tests/FluentCMS.E2eTests -t ./src/Tests/FluentCMS.E2eTests\bin\Debug\net8.0\TestExecution.json --output LivingDoc.Html
```

- For guidance on installation and an overview of functionalities, kindly consult the [LivingDoc Official Documentation](https://docs.specflow.org/projects/specflow-livingdoc/en/latest/LivingDocGenerator/Generating-Documentation.html).
  
  -Please ensure that tests are executed prior to report generation, as the `TestExecution.json` file is essential for creating the report.

## Contributing

We welcome contributions! If you're interested in improving FluentCMS, please read our [CONTRIBUTING.md](./CONTRIBUTING.md) guide.

## Roadmap

- Enhance AI recommendations with user feedback loops.
- Extend headless capabilities with more API integrations.
- Add multi-language support.
- Develop a marketplace for plugins and themes.
- ... *(Your future plans for the project)*

## License

This project is licensed under the MIT License - see the [LICENSE](./LICENSE) file for details.

## Acknowledgments

- Thanks to the ASP.NET community for continuous inspiration and support.
- ... *(Any other acknowledgments or shout-outs you want to include)*
