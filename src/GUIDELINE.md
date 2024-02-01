# Guideline For Development

A guideline for developing in Blazor.

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
