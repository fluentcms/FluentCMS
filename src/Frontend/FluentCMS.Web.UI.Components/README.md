# FluentCMS Component Library

A custom Blazor-based component library designed with Flowbite styles, providing a rich set of reusable and modular UI components. These components are built based on our needs in FluentCMS.

## Components
The library includes the following components:

### General Components
- [x] Accordion
- [x] Alert
- [x] Avatar
- [x] Badge
- [x] Breadcrumb
- [x] Button
- [x] Card
- [x] CloseButton
- [x] Confirm
- [x] DataTable
- [x] Divider
- [x] Dropdown
- [x] Grid & GridItem
- [x] Icon
- [x] Indicator
- [x] InlineEditor
- [x] Modal
- [x] Pagination
- [x] Spacer
- [x] Spinner
- [x] Stack
- [x] Stepper
- [x] Tabs
- [x] Toast
- [x] Tooltip
- [x] Typography

### Form Components
- [x] Autocomplete
- [x] Checkbox
- [x] CheckboxGroup
- [x] DateInput
- [x] FileUpload
- [x] Input
- [x] MarkdownEditor
- [x] NumberInput
- [x] TreeSelector
- [x] RichTextEditor
- [x] RadioGroup
- [x] Select
- [x] Switch
- [x] Textarea
- [x] Label
- [x] FormField

## Getting Started

### Installation

1. Install nuget package:

    ```bash
    dotnet package add FluentCMS.Web.UI.Components
    ```

2. Register UI Components service in `Program.cs` file:

    ```cs
    builder.Services.AddUIComponents();
    ```

3. Add link to Styles in head section of the `App.razor` file:

    ```razor
    <link rel="stylesheet" href="/_content/FluentCMS.Web.UI.Components/css/flowbite.min.css">
    ```
4. Use components in your project:
    ```razor
    <Button Type="ButtonType.Submit" Color="Color.Primary">Submit</Button>
    <Button @onclick="() => Console.WriteLine("Cancelled")">Cancel</Button>
    ```

5. Run the application:

   ```bash
   dotnet run
   ```

## Contribution
We welcome contributions! If you'd like to contribute, please open an issue or submit a pull request.

## License
This project is open-source and available under the MIT License.