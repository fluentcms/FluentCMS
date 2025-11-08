# FluentCMS UI Components - Building New Components

This guide explains how to create new reusable components for the FluentCMS Blazor component library.

## Component Structure Overview

The component library follows a consistent pattern where each component consists of:
- A `.razor` file containing the component markup and code
- Optional `.cs` files for enums and additional classes
- A corresponding SCSS file in `Styles/tabler/` for styling

## Base Classes

All components inherit from one of two base classes:

### BaseComponent
- Abstract class with common functionality for all components
- Provides automatic CSS class generation based on properties
- Includes `Visible`, `Class`, and `AdditionalAttributes` parameters
- Handles CSS naming conventions (kebab-case conversion)

### BaseComponentWithContent
- Extends `BaseComponent`
- Adds `ChildContent` parameter for components with content inside

## Creating a New Component

### Step 1: Create the Component Directory and Files

Create a new directory under `FluentCMS.Web.UI.Components/` with your component name:

```
Components/
  └── YourComponent/
      ├── YourComponent.razor
      └── YourComponent.cs (if needed for enums)
```

### Step 2: Implement the Razor Component

Every component should start with:

```razor
@inherits BaseComponentWithContent  // or BaseComponent if no child content needed

@namespace FluentCMS.Web.UI.Components

<!-- Your HTML markup here -->
<div @attributes="AdditionalAttributes" class="@GetClasses()">
    @ChildContent  <!-- If inheriting from BaseComponentWithContent -->
</div>

@code {
    // Component parameters and logic here
}
```

#### Basic Component Example (Simple Badge):

```razor
@inherits BaseComponentWithContent

@namespace FluentCMS.Web.UI.Components

<span @attributes="AdditionalAttributes" class="@GetClasses()">
    @ChildContent
</span>

@code {
    [Parameter]
    [CssProperty]
    public Color? Color { get; set; }

    [Parameter]
    [CssProperty]
    public bool? Light { get; set; }

    [Parameter]
    [CssProperty]
    public bool? Pill { get; set; }
}
```

### Step 3: Define Component Parameters

#### Standard Parameters (Automatically Available):
- `bool Visible = true` - Controls component visibility
- `string? Class` - Additional CSS classes
- `IReadOnlyDictionary<string, object>? AdditionalAttributes` - Additional HTML attributes
- `string? CssName` - Override default CSS class name
- `RenderFragment? ChildContent` - If inheriting from BaseComponentWithContent

#### CSS Properties with Automatic Styling:

Use the `[CssProperty]` attribute to automatically generate CSS classes:

```razor
@code {
    [Parameter]
    [CssProperty]
    public Color? Color { get; set; }  // Generates: f-{componentname}-color-{value}

    [Parameter]
    [CssProperty]
    public bool? Outline { get; set; } // Generates: f-{componentname}-outline-true

    [Parameter]
    [CssProperty]
    public string? Size { get; set; }  // Generates: f-{componentname}-size-{value}
}
```

#### Regular Parameters (No Automatic CSS):

```razor
@code {
    [Parameter]
    public string Type { get; set; } = "button";  // Used in HTML attributes

    [Parameter]
    public EventCallback OnClick { get; set; }
}
```

### Step 4: Create Supporting Enums

If your component has specific enum values, create them in the component's `.cs` file:

```csharp
namespace FluentCMS.Web.UI.Components;

public enum YourComponentSize
{
    Small,
    Large,
    ExtraLarge
}

public enum YourComponentType
{
    Default,
    Primary,
    Secondary
}
```

### Step 5: Implement Styling with SCSS

Create a new SCSS file in `Styles/tabler/_yourcomponent.scss`:

#### Basic Structure:

```scss
$yourcomponent: $prefix + 'yourcomponent';  // $prefix is "f-"

.#{$yourcomponent} {
    // Base component styles extending Tabler classes
    @extend .your-tabler-class;

    // Theme color variants
    @each $key, $value in $fluent-colors {
        &-color-#{$key} {
            @extend .bg-#{$key} !optional;
            @extend .text-#{$key}-fg !optional;
        }
    }

    // Custom property variants
    &-pill-true {
        @extend .rounded-pill;
    }

    // Nested selectors for complex properties
    &-size {
        &-small {
            @extend .your-small-class;
        }

        &-large {
            @extend .your-large-class;
        }
    }
}
```

#### Available Variables:

- `$prefix`: "f-" (defined in core.scss)
- `$fluent-colors`: Map of theme colors (primary, secondary, success, danger, etc.)
- `$theme-colors`: Full theme color map from Tabler

### Step 6: Add Component to Build System

#### Update core.scss
Add your component import to `Styles/tabler/core.scss`:

```scss
@import './_alert.scss';
@import './_badge.scss';
@import './_button.scss';
@import './_card.scss';
@import './_yourcomponent.scss';  // Add your component here
```

#### Update package.json (if needed)
If you add new dependencies for building styles, update the `Styles/package.json` and configure npm scripts.

## Advanced Patterns

### Event Handling

```razor
@code {
    [Parameter]
    public EventCallback<YourEventArgs> OnSomething { get; set; }

    private async Task HandleClick()
    {
        if (OnSomething.HasDelegate)
        {
            await OnSomething.InvokeAsync(new YourEventArgs { /* data */ });
        }
    }
}
```

### Complex Properties

For properties that need custom logic:

```razor
@code {
    private string ComputedSize => Size?.ToString().ToLower() ?? "medium";

    public override string GetClasses()
    {
        var classes = base.GetClasses();
        // Add custom logic here
        if (/* custom condition */)
        {
            classes = $"{classes} custom-class";
        }
        return classes;
    }
}
```

### Component-Specific HTML Attributes

```razor
<button @attributes="AdditionalAttributes" class="@GetClasses()" type="@Type.ToString().ToLower()" disabled="@Disabled">
    @ChildContent
</button>
```

## CSS Class Naming Convention

The system automatically generates CSS classes in the format:
```
f-{component-name}-{property-name}-{property-value}
```

Examples:
- Color.Primary → `f-badge-color-primary`
- Size.Small → `f-button-size-small`
- Light = true → `f-badge-light-true`

All class names use kebab-case (PascalCase → kebab-case conversion).

## Best Practices

### Component Design
1. Follow the existing naming conventions
2. Use the Color enum for colors instead of custom strings
3. Keep component APIs consistent with existing components
4. Document parameters and their purposes

### Performance
1. Use `CssProperty` for styling variants to enable automatic class generation
2. Avoid creating new component classes unnecessarily - extend existing ones
3. Keep component logic simple; complex logic belongs in parent components

### Styling
1. Always extend from Tabler classes when possible
2. Use the established color system (`$fluent-colors`)
3. Keep SCSS files organized and commented
4. Test visual consistency across different color schemes

## Example: Complete Button Component

### Button.razor
```razor
@inherits BaseComponentWithContent

@namespace FluentCMS.Web.UI.Components

<button @attributes="AdditionalAttributes" class="@GetClasses()" disabled="@Disabled" type="@Type.ToString().ToLower()">
    @ChildContent
</button>

@code {
    [Parameter]
    [CssProperty]
    public bool? Block { get; set; }

    [Parameter]
    [CssProperty]
    public Color? Color { get; set; }

    [Parameter]
    [CssProperty]
    public bool? Disabled { get; set; }

    [Parameter]
    [CssProperty]
    public bool? Ghost { get; set; }

    [Parameter]
    [CssProperty]
    public bool? Outline { get; set; }

    [Parameter]
    [CssProperty]
    public ButtonSize? Size { get; set; }

    [Parameter]
    public ButtonType Type { get; set; } = ButtonType.Button;

    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }
}
```

### Button.cs
```csharp
namespace FluentCMS.Web.UI.Components;

public enum ButtonSize
{
    Small,
    Large
}

public enum ButtonType
{
    Button,
    Submit,
    Reset
}
```

### Styles/tabler/_button.scss
```scss
$button: $prefix + "button";

.#{$button} {
    @extend .btn;

    &-block-true {
        @extend .w-100;
    }

    &-ghost-true {
        @extend .btn-ghost;
    }

    &-outline-true {
        @extend .btn-outline;
    }

    &-size {
        &-small {
            @extend .btn-sm;
        }

        &-large {
            @extend .btn-lg;
        }
    }
}

@each $key, $value in $fluent-colors {
    .#{$button}-color-#{$key} {
        @extend .btn-#{$key};
    }
}
```

## Testing Your Component

Use the TestApp project to test your components:

1. Add your component to test pages
2. Build the library: `dotnet build`
3. Run the test app: `dotnet run` in the TestApp directory
4. Verify styling by running the build process for SCSS

## Troubleshooting

### CSS Classes Not Applied
- Verify `@extends` directives in your SCSS file
- Check that the SCSS file is imported in core.scss
- Ensure the component is using the correct base class

### Component Not Rendering
- Check that you're inheriting from the correct base class
- Verify namespace and component registration
- Ensure Visible parameter is not set to false

### Build Errors
- Check C# syntax and enum definitions
- Verify all using statements are included in _GlobalUsings.cs
- Ensure project references are updated