# Guideline For Development

A guideline for developing in Blazor.

## Commit naming convention

Use [https://www.conventionalcommits.org/en/v1.0.0](https://www.conventionalcommits.org/en/v1.0.0) for commit messages.

## Abbreviation

Use a complete word(s) to define a variables, methods, etc.

```c#
// DO
try {}
catch (Exception exception) {}

// DON'T
try {}
catch (Exception ex) {}
```

## Attributes sorting

Sort attributes alphabetically.

```c#
<Input @bind-Value="Model"
       Label="Label"
       Placeholder="Placeholder"
       Required="true"
       OnChange="OnChange"
       OnInput="OnInput" />
```

## Boolean Properties

DON'T set `default` to properties.

```c#
// DO
bool Count { get; set; }

// DON'T
bool Count { get; set; } = default;
```

## Boolean Attributes

Set explicit `true` or `false` to boolean properties.

```c#
// DO
<Input Required="true" />

// DON'T
<Input Required />
```

## Extra breaking line

```c#
// DO
@code {
    [Inject]
    UserClient UserClient { get; set; } = default!;
}

// DON'T
@code {

    [Inject]
    UserClient UserClient { get; set; } = default!;

}
```

## Injectable services

Set `default!` to injected services.

```c#
// DO
[Inject]
UserClient UserClient { get; set; } = default!;

// DON'T
[Inject]
UserClient? UserClient { get; set; }
```

## Multiple Attributes

Split attributes into multiple lines for tags with more than one attribute.

```c#
// DO
<Input Label="Email"
       Required="true" />

// DON'T
<Input Label="Email" Required="true" />
```

## Integer

DON'T set `0` to int properties.

```c#
// DO
int Count { get; set; }

// DON'T
int Count { get; set; } = 0;
```

## String

Use nullable string insted of `string.Empty`.

```c#
// DO
string? Message { get; set; }

// DON'T
string Message { get; set; } = string.Empty;
```

## Todo comments

In areas with technical debt or uncertainty, utilize the term `TODO` as a comment.

```c#
// TODO: Remove or make it better
[Parameter]
public bool Sub { get; set; }
```

## Naming

Microsoft already has a set of well established naming conventions [Naming rules](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names#naming-rules)

When writing C# code, following consistent naming conventions is essential for readability, maintainability, and collaboration within a development team. Here are the key guidelines for naming identifiers:

1- Interface Names:

-   Start with a capital “I.”
-   Example: `public interface IMyInterface { ... }`

2- Attribute Types:

-   End with the word “Attribute.”
-   Example: `public class MyCustomAttribute : Attribute { ... }`

3- Enum Types:

-   Use a singular noun for non-flag enums and a plural noun for flag enums.
-   Example `(non-flag): public enum DayOfWeek { ... }`
-   Example `(flag): public enum Permissions { ... }`

4- General Identifiers:

-   Avoid using two consecutive underscores (\_\_) in identifiers (reserved for compiler-generated names).
-   Use meaningful and descriptive names for variables, methods, and classes.
-   Prefer clarity over brevity.
-   Example: `int itemCount = 42;`

5- Casing Conventions:

-   Use PascalCase for class names and method names.
-   Use camelCase for method arguments, local variables, and private fields.
-   Use PascalCase for constant names (both fields and local constants).
-   Example:

```csharp
public class MyClass
{
    public void MyMethod(int parameterValue)
    {
        const int MaxItems = 100;
        // ...
    }
}
```

6- Private Instance Fields:

-   Start with an underscore (`_`).

-   Example: `private int _count;`

7- Static Fields:

-   Start with `s_` (not part of the default Visual Studio behavior but configurable in editorconfig).

-   Example: `private static readonly string s_defaultName = "John";`

8- Abbreviations and Acronyms:

-   Avoid using them in names unless they are widely known and accepted.

-   Example: `XmlHttpRequest` instead of `XMLHTTPRequest`

9- Namespaces:

-   Use meaningful and descriptive namespaces following reverse domain name notation.

-   Example: `namespace MyCompany.MyProject.Utilities;`

10- Assembly Names:

-   Choose assembly names that represent the primary purpose of the assembly.

-   Example: `MyCompany.MyProject.Core`

11- Single-Letter Names:

-   Avoid using single-letter names except for simple loop counters.
