# FluentCMS.Web.UI.Components - AI Development Guide

> **Purpose**: This document provides comprehensive guidance for AI assistants working on the FluentCMS Blazor component library. It explains the architecture, conventions, and patterns used throughout the codebase.

## Table of Contents
1. [Project Overview](#project-overview)
2. [Architecture](#architecture)
3. [CSS Class Generation System](#css-class-generation-system)
4. [Component Development](#component-development)
5. [Styling System](#styling-system)
6. [Existing Components](#existing-components)
7. [File Structure](#file-structure)
8. [Coding Conventions](#coding-conventions)
9. [Common Tasks](#common-tasks)

---

## Project Overview

### Technology Stack
- **Framework**: .NET 9.0, ASP.NET Core Blazor
- **Target**: Browser (Blazor WebAssembly/Server)
- **CSS Framework**: Tabler Core v1.4.0
- **CSS Processor**: SASS (Dart Sass v1.93.2)
- **Package**: Microsoft.AspNetCore.Components.Web v9.0.9

### Project Purpose
FluentCMS.Web.UI.Components is a reusable Blazor component library that provides UI components with automatic CSS class generation based on component properties. It leverages the Tabler CSS framework for consistent, professional styling.

### Key Features
- **Automatic CSS class generation** from component properties
- **Performance-optimized** with reflection caching and compiled expression trees
- **Tabler CSS integration** for professional UI design
- **Convention-based naming** (kebab-case CSS classes)
- **Extensible base component system**

---

## Architecture

### Base Component Hierarchy

```
ComponentBase (Blazor built-in)
    ↓
BaseComponent (abstract)
    ↓
BaseComponentWithContent
    ↓
Concrete Components (Button, Badge, Avatar, etc.)
```

### Core Components

#### 1. `BaseComponent` (Abstract)
**Location**: `Base/BaseComponent.razor.cs` + `Base/BaseComponent.razor`

**Purpose**: Provides core functionality for all components including:
- Visibility control
- CSS class generation
- Additional attributes support
- Property-to-CSS mapping

**Key Properties**:
```csharp
public bool Visible { get; set; } = true;          // Controls component visibility
public string? Class { get; set; }                 // User-provided CSS classes
public string? CssName { get; set; }               // Override default component name
public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
```

**Key Method**:
```csharp
public virtual string GetClasses()
```
- Generates CSS classes based on:
  1. Component name (e.g., `f-button`)
  2. Properties marked with `[CssProperty]` (e.g., `f-button-size-large`)
  3. User-provided `Class` parameter

**Template** (`BaseComponent.razor`):
```razor
@if (Visible)
{
    @BuildContent
}
```

#### 2. `BaseComponentWithContent`
**Location**: `Base/BaseComponentWithContent.razor`

**Purpose**: Extends `BaseComponent` for components that accept child content.

```razor
@inherits BaseComponent

@code {
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override RenderFragment BuildContent => ChildContent ?? (__builder => { });
}
```

#### 3. `IBaseComponent` Interface
**Location**: `Base/IBaseComponent.cs`

Defines the contract for all base components.

### Supporting Infrastructure

#### `CssPropertyAttribute`
**Location**: `Base/CssPropertyAttribute.cs`

Marker attribute for properties that should generate CSS classes.

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class CssPropertyAttribute : Attribute { }
```

**Usage Example**:
```csharp
[Parameter]
[CssProperty]  // ← This property will generate CSS classes
public Color Color { get; set; } = Color.Primary;
```

#### `StringExtensions`
**Location**: `Base/StringExtensions.cs`

Provides PascalCase to kebab-case conversion.

```csharp
"ButtonSize" → "button-size"
"PrimaryColor" → "primary-color"
```

---

## CSS Class Generation System

### How It Works

The system automatically generates CSS classes based on component properties marked with `[CssProperty]`.

### Generation Pattern

```
f-{component-name}-{property-name}-{property-value}
```

All parts are converted to kebab-case.

### Example

Given a Button component:
```razor
<Button Color="Primary" Size="Large" Block="true">
    Click Me
</Button>
```

Generated classes:
```
f-button                    ← Base component class
f-button-color-primary      ← Color property
f-button-size-large         ← Size property
f-button-block-true         ← Block property
```

### Performance Optimizations

#### 1. Reflection Caching
```csharp
private static readonly ConcurrentDictionary<Type, CssPropertyMetadata[]> _cssPropertyCache = new();
```

Properties with `[CssProperty]` are scanned once per component type and cached.

#### 2. Compiled Expression Trees
```csharp
var lambda = Expression.Lambda<Func<IBaseComponent, object?>>(
    convertResult, 
    componentParameter
).Compile();
```

Property accessors are compiled to delegates for fast repeated access (avoids slow reflection calls).

### CSS Prefix

All component classes use the prefix `f-` (defined as `CSS_PREFIX` constant).

**Why?** Namespace isolation to prevent conflicts with other CSS frameworks.

---

## Component Development

### Creating a New Component

#### Step 1: Create Component Directory
```
ComponentName/
  ├── ComponentName.razor
  ├── ComponentType.cs (if needed for enums)
  └── ComponentSize.cs (if needed for enums)
```

#### Step 2: Choose Base Class

**Use `BaseComponentWithContent`** when:
- Component accepts child content/markup
- Examples: Button, Badge, Card, Modal

**Use `BaseComponent`** when:
- Component is self-contained with no child content
- Component builds its own markup
- Example: Icon, Spinner, Divider

#### Step 3: Component Implementation

**Example: Creating a new Alert component**

```razor
@inherits BaseComponentWithContent
@namespace FluentCMS.Web.UI.Components

<div @attributes="AdditionalAttributes" class="@GetClasses()" role="alert">
    @ChildContent
</div>

@code {
    [Parameter]
    [CssProperty]
    public Color Color { get; set; } = Color.Primary;
    
    [Parameter]
    [CssProperty]
    public bool Dismissible { get; set; }
    
    [Parameter]
    public EventCallback OnDismiss { get; set; }
}
```

#### Step 4: Create SCSS File

**Location**: `Styles/tabler/ComponentName.scss`

```scss
$alert: $prefix + "alert";

.#{$alert} {
    @extend .alert;
    
    &-dismissible-true {
        @extend .alert-dismissible;
    }
}

@each $key, $value in $theme-colors {
    .#{$alert}-color-#{$key} {
        @extend .alert-#{$key};
    }
}
```

#### Step 5: Import SCSS in App.scss

```scss
@import './ComponentName.scss';
```

#### Step 6: Build CSS

```bash
cd Styles
npm run build
```

### Adding Properties to Existing Components

1. Add property with `[Parameter]` and optionally `[CssProperty]`
2. If using `[CssProperty]`, add corresponding SCSS rules
3. Rebuild CSS
4. Test the component

---

## Styling System

### SASS Compilation Workflow

**Source**: `Styles/tabler/App.scss`  
**Output**: `wwwroot/css/app.min.css`

### Build Commands

```json
{
  "build": "sass --load-path=node_modules ./tabler/App.scss:../wwwroot/css/app.min.css",
  "watch": "sass --load-path=node_modules --watch ./tabler/App.scss:../wwwroot/css --style=expanded"
}
```

**Development**:
```bash
cd Styles
npm run watch
```

**Production**:
```bash
cd Styles
npm run build
```

### App.scss Structure

```scss
// 1. Define FluentCMS prefix
$prefix: "f-";

// 2. Optional Tabler variable overrides
// $primary: #0ea5e9;

// 3. Import Tabler core (provides all base classes)
@import "@tabler/core/scss/tabler";

// 4. Import component-specific styles
@import './Button.scss';
@import './Badge.scss';
// ... more components
```

### Component SCSS Pattern

Each component has its own SCSS file that:
1. Defines a component variable with prefix
2. Extends Tabler classes
3. Maps CssProperty values to CSS classes

**Example** (`Button.scss`):
```scss
$button: $prefix + "button";  // "f-button"

.#{$button} {
    @extend .btn;  // Use Tabler's .btn class
    
    &-block-true {
        @extend .w-100;  // Full width when Block=true
    }
}

// Generate color classes for all theme colors
@each $key, $value in $theme-colors {
    .#{$button}-color-#{$key} {
        @extend .btn-#{$key};
    }
}
```

### Tabler Integration

**How it works**:
1. Tabler provides CSS classes (`.btn`, `.btn-primary`, etc.)
2. FluentCMS components extend these classes
3. Components generate classes like `f-button-color-primary`
4. SCSS `@extend` maps `f-button-color-primary` → `.btn-primary`

**Benefits**:
- Leverage Tabler's complete design system
- Maintain FluentCMS naming convention
- Easy to customize with Tabler variables

---

## Existing Components

### Button Component

**Location**: `Button/Button.razor`

**Parameters**:
```csharp
[Parameter] [CssProperty] public bool Block { get; set; }           // Full width
[Parameter] [CssProperty] public Color Color { get; set; }          // Button color
[Parameter] [CssProperty] public bool Disabled { get; set; }        // Disabled state
[Parameter] [CssProperty] public bool Ghost { get; set; }           // Ghost variant
[Parameter] [CssProperty] public bool Link { get; set; }            // Link variant
[Parameter] [CssProperty] public bool Outline { get; set; }         // Outline variant
[Parameter] [CssProperty] public ButtonSize Size { get; set; }      // Button size
[Parameter] public ButtonType Type { get; set; }                    // HTML type
[Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }
```

**Enums**:
- `ButtonSize`: Default, Small, Medium, Large
- `ButtonType`: Button, Submit, Reset

**Usage**:
```razor
<Button Color="Color.Primary" Size="ButtonSize.Large" Block="true" OnClick="HandleClick">
    Click Me
</Button>
```

**Generated Classes**:
```
f-button
f-button-color-primary
f-button-size-large
f-button-block-true
```

### Badge Component

**Location**: `Badge/Badge.razor`

**Parameters**:
```csharp
[Parameter] [CssProperty] public Color Color { get; set; } = Color.Default;
```

**Usage**:
```razor
<Badge Color="Color.Success">New</Badge>
```

### Avatar Component

**Location**: `Avatar/Avatar.razor`

**Current State**: Basic structure only, minimal parameters

**Markup**:
```razor
<div @attributes="AdditionalAttributes" class="@GetClasses()">
    <span>@ChildContent</span>
</div>
```

### Color Enum

**Location**: `Color.cs`

```csharp
public enum Color
{
    Default, Primary, Secondary, Success, 
    Danger, Warning, Info, Light, Dark, 
    White, Transparent
}
```

Maps to Tabler theme colors.

---

## File Structure

```
FluentCMS.Web.UI.Components/
├── _GlobalUsings.cs              # Global using directives
├── _Imports.razor                # Razor imports
├── Color.cs                      # Shared Color enum
├── FluentCMS.Web.UI.Components.csproj
│
├── Base/                         # Base component infrastructure
│   ├── BaseComponent.razor
│   ├── BaseComponent.razor.cs
│   ├── BaseComponentWithContent.razor
│   ├── CssPropertyAttribute.cs
│   ├── IBaseComponent.cs
│   └── StringExtensions.cs
│
├── Button/                       # Button component
│   ├── Button.razor
│   ├── ButtonSize.cs
│   └── ButtonType.cs
│
├── Badge/                        # Badge component
│   └── Badge.razor
│
├── Avatar/                       # Avatar component
│   └── Avatar.razor
│
├── Styles/                       # SASS source files
│   ├── package.json
│   ├── package-lock.json
│   └── tabler/
│       ├── App.scss             # Main SCSS entry point
│       └── Button.scss          # Component SCSS
│
├── wwwroot/                     # Compiled output
│   └── css/
│       ├── app.min.css          # Compiled CSS
│       └── app.min.css.map
│
├── bin/                         # Build output
└── obj/                         # Build intermediates
```

---

## Coding Conventions

### Naming Conventions

#### C# Code
- **Components**: PascalCase (e.g., `Button`, `Badge`, `Avatar`)
- **Properties**: PascalCase (e.g., `Color`, `Size`, `Block`)
- **Enums**: PascalCase for type and values (e.g., `ButtonSize.Large`)

#### CSS Classes
- **All lowercase with hyphens** (kebab-case)
- **Pattern**: `f-component-property-value`
- **Examples**: `f-button`, `f-button-size-large`, `f-badge-color-primary`

#### SCSS Variables
- **Lowercase with hyphens**
- **Pattern**: `$component: $prefix + "component-name"`
- **Example**: `$button: $prefix + "button"` results in `"f-button"`

### Component Development Patterns

#### 1. Always Use Namespace
```razor
@namespace FluentCMS.Web.UI.Components
```

#### 2. Inherit from Appropriate Base
```razor
@inherits BaseComponentWithContent  // For components with content
@inherits BaseComponent             // For self-contained components
```

#### 3. Apply Additional Attributes
```razor
<div @attributes="AdditionalAttributes" class="@GetClasses()">
```

#### 4. Mark CSS-Generating Properties
```csharp
[Parameter]
[CssProperty]  // ← Generates CSS classes
public Color Color { get; set; }
```

#### 5. Non-CSS Parameters
```csharp
[Parameter]  // No [CssProperty]
public EventCallback OnClick { get; set; }
```

### SCSS Development Patterns

#### 1. Component Variable
```scss
$component: $prefix + "component-name";
```

#### 2. Base Class Extension
```scss
.#{$component} {
    @extend .tabler-equivalent-class;
}
```

#### 3. Boolean Properties
```scss
&-property-true {
    @extend .tabler-class;
}
```

#### 4. Enum Properties with Theme Colors
```scss
@each $key, $value in $theme-colors {
    .#{$component}-property-#{$key} {
        @extend .tabler-#{$key};
    }
}
```

---

## Common Tasks

### Task 1: Add a New Component

1. **Create directory**: `ComponentName/`
2. **Create component file**: `ComponentName/ComponentName.razor`
   ```razor
   @inherits BaseComponentWithContent
   @namespace FluentCMS.Web.UI.Components

   <element @attributes="AdditionalAttributes" class="@GetClasses()">
       @ChildContent
   </element>

   @code {
       // Add parameters here
   }
   ```
3. **Create SCSS file**: `Styles/tabler/ComponentName.scss`
4. **Import in App.scss**: `@import './ComponentName.scss';`
5. **Build CSS**: `cd Styles && npm run build`

### Task 2: Add a Property to Existing Component

1. **Add parameter** to component `.razor` file:
   ```csharp
   [Parameter]
   [CssProperty]
   public PropertyType PropertyName { get; set; }
   ```
2. **Add SCSS rules** if `[CssProperty]` is used:
   ```scss
   &-property-name-value {
       @extend .appropriate-tabler-class;
   }
   ```
3. **Rebuild CSS**: `cd Styles && npm run build`

### Task 3: Change Component CSS Output

Modify the component's SCSS file in `Styles/tabler/`, then rebuild:
```bash
cd Styles
npm run build
```

### Task 4: Override Tabler Variables

Edit `Styles/tabler/App.scss` **before** the `@import "@tabler/core/scss/tabler"` line:
```scss
$prefix: "f-";

// Override Tabler variables here
$primary: #0ea5e9;
$border-radius: 0.5rem;

@import "@tabler/core/scss/tabler";
```

### Task 5: Debug CSS Class Generation

1. Check if property has `[CssProperty]` attribute
2. Verify property name and value (will be converted to kebab-case)
3. Check SCSS file for corresponding rule
4. Ensure CSS was rebuilt after SCSS changes
5. Inspect browser to see actual classes applied

### Task 6: Test a Component

Create a test Blazor page:
```razor
@page "/test-component"

<h1>Component Test</h1>

<Button Color="Color.Primary" Size="ButtonSize.Large">
    Test Button
</Button>

<Badge Color="Color.Success">Badge Test</Badge>
```

---

## Performance Considerations

### 1. Reflection Caching
`BaseComponent` caches reflection metadata per component type:
```csharp
private static readonly ConcurrentDictionary<Type, CssPropertyMetadata[]> _cssPropertyCache
```

**Result**: Property scanning happens once per component type, not per instance.

### 2. Compiled Expression Trees
Property accessors are compiled to delegates:
```csharp
Expression.Lambda<Func<IBaseComponent, object?>>(...).Compile()
```

**Result**: Fast property value retrieval without repeated reflection.

### 3. CSS Build Optimization
CSS is pre-compiled during build, not at runtime.

---

## Global Usings

**Location**: `_GlobalUsings.cs`

```csharp
global using FluentCMS.Web.UI.Components;
global using Microsoft.AspNetCore.Components;
global using Microsoft.AspNetCore.Components.Forms;
global using Microsoft.AspNetCore.Components.Web;
global using Microsoft.JSInterop;
global using System.Collections.Concurrent;
global using System.Linq.Expressions;
global using System.Reflection;
```

These namespaces are available in all C# files without explicit `using` statements.

---

## Important Notes for AI Assistants

### When Adding Components

1. ✅ **DO** inherit from `BaseComponentWithContent` for most components
2. ✅ **DO** use `[CssProperty]` for properties that should generate CSS classes
3. ✅ **DO** create corresponding SCSS files
4. ✅ **DO** extend Tabler classes when possible
5. ✅ **DO** rebuild CSS after SCSS changes

### When Modifying Components

1. ✅ **DO** check if property needs `[CssProperty]`
2. ✅ **DO** add SCSS rules for new `[CssProperty]` properties
3. ✅ **DO** test the component after changes
4. ❌ **DON'T** forget to rebuild CSS

### CSS Class Generation

1. ✅ Component name, property names, and values are **auto-converted to kebab-case**
2. ✅ All classes start with `f-` prefix
3. ✅ Pattern: `f-{component}-{property}-{value}`
4. ❌ **DON'T** manually construct CSS class strings in components (use `GetClasses()`)

### Performance

1. ✅ Reflection is cached per type
2. ✅ Property accessors are compiled
3. ✅ Thread-safe with `ConcurrentDictionary`
4. ❌ **DON'T** add unnecessary reflection or dynamic code

---

## Questions & Troubleshooting

### CSS Classes Not Appearing?
1. Is property marked with `[CssProperty]`?
2. Did you rebuild CSS? (`cd Styles && npm run build`)
3. Is there a corresponding SCSS rule?
4. Check browser DevTools for actual classes

### Component Not Rendering?
1. Is `Visible` parameter true? (default is true)
2. Does component inherit from correct base?
3. Is `@namespace` directive present?

### SCSS Compilation Errors?
1. Check SCSS syntax
2. Ensure Tabler variables are imported first
3. Verify `@import` paths are correct

---

## Summary

This Blazor component library demonstrates:
- **Intelligent architecture** with automatic CSS class generation
- **Performance optimization** through caching and compiled expressions
- **Integration with Tabler** for professional UI components
- **Convention over configuration** for rapid development
- **Extensible design** for easy addition of new components

When working on this project, always consider:
1. Where does the component fit in the hierarchy?
2. What properties should generate CSS classes?
3. How do I map to Tabler's existing classes?
4. Did I rebuild the CSS after changes?

---

**Document Version**: 1.0  
**Last Updated**: 2025-10-28  
**Project**: FluentCMS.Web.UI.Components
