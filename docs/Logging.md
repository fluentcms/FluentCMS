# Logging

## HTTP Logging for API 

The HTTP Logging Middleware `HttpLoggingMiddleware.cs` is responsible for capturing detailed information about every HTTP request and response passing through the application. This includes logging the request and response metadata, body (if enabled), and any exceptions that occur during processing. The logged data can be stored in a database for auditing, debugging, or analytical purposes.

### Key Features

- Logs HTTP request and response metadata such as headers, method, URL, and body.
- Measures the duration of request processing.
- Captures exception details if an error occurs during processing.
- Configurable options to enable or disable logging of request and response bodies.

### How It Works

1. The middleware intercepts all HTTP requests and responses.
2. If configured, it reads the request and response bodies.
3. Exceptions encountered during request processing are captured and logged.
4. Asynchronous logging ensures minimal performance impact.
5. The collected data is structured into an `HttpLog` object and saved to the database.

---

### Configuration Properties

The `HttpLogging` section in the appsettings.json file allows you to customize the behavior of the HTTP logging middleware and background processor. Below are the available configuration properties:

| Property               | Type    | Default Value | Description                                                                 |
|------------------------|---------|---------------|-----------------------------------------------------------------------------|
| `Enable`               | `bool`  | `false`       | Toggles the logging functionality on or off.                               |
| `EnableRequestBody`    | `bool`  | `false`       | Enables or disables logging of the HTTP request body.                      |
| `EnableResponseBody`   | `bool`  | `false`       | Enables or disables logging of the HTTP response body.                     |
| `BatchSize`            | `int`   | `50`          | Specifies the number of log entries to process in a single batch.          |

### Example `appsettings.json` Configuration

```json
"HttpLogging": {
    "Enable": true,
    "EnableRequestBody": false,
    "EnableResponseBody": false,
    "BatchSize": 50
}
```

## HttpLog Details

The `HttpLog` class is used to store detailed information about HTTP requests, responses, and exceptions encountered during processing. This data can be used for debugging, auditing, or performance analysis.

### General Information

| Property              | Type   | Description                                                   |
|-----------------------|--------|---------------------------------------------------------------|
| `StatusCode`          | `int`  | The HTTP status code returned in the response.                |
| `Duration`            | `long` | The total time taken to process the request, in milliseconds. |
| `AssemblyName`        | `string` | The name of the assembly handling the request.              |
| `AssemblyVersion`     | `string` | The version of the assembly handling the request.           |
| `ProcessId`           | `int`  | The process ID of the application handling the request.       |
| `ProcessName`         | `string` | The name of the process handling the request.               |
| `ThreadId`            | `int`  | The thread ID of the thread handling the request.             |
| `MemoryUsage`         | `long` | The memory usage (in bytes) at the time of logging.           |
| `MachineName`         | `string` | The name of the machine handling the request.               |
| `EnvironmentName`     | `string` | The environment name (e.g., Development, Production).        |
| `EnvironmentUserName` | `string` | The username of the user running the application.           |
| `IsAuthenticated`     | `bool` | Indicates whether the request is authenticated.               |
| `Language`            | `string` | The language preference of the user making the request.     |
| `SessionId`           | `string` | The session ID associated with the request.                 |
| `StartDate`           | `DateTime` | The start date and time of the request.                   |
| `TraceId`             | `string` | A unique identifier for tracing the request.               |
| `UniqueId`            | `string` | A unique identifier associated with the request.           |
| `UserId`              | `Guid` | The ID of the user making the request.                       |
| `UserIp`              | `string` | The IP address of the user making the request.             |
| `Username`            | `string` | The username of the authenticated user.                    |
| `ApiTokenKey`         | `string` | The API token key used for authentication (if applicable). |

---

### Request Details

| Property           | Type                    | Description                                                  |
|--------------------|-------------------------|--------------------------------------------------------------|
| `ReqUrl`           | `string`               | The full URL of the request.                                 |
| `ReqProtocol`      | `string`               | The protocol used in the request (e.g., HTTP/1.1).           |
| `ReqMethod`        | `string`               | The HTTP method of the request (e.g., GET, POST).            |
| `ReqScheme`        | `string`               | The scheme used in the request (e.g., http, https).          |
| `ReqPathBase`      | `string`               | The base path of the request.                                |
| `ReqPath`          | `string`               | The path of the request.                                     |
| `QueryString`      | `string`               | The query string of the request.                             |
| `ReqContentType`   | `string`               | The content type of the request.                             |
| `ReqContentLength` | `long?`                | The content length of the request body (if available).       |
| `ReqBody`          | `string?`             | The body of the request (if enabled).                        |
| `ReqHeaders`       | `Dictionary<string, string>` | The HTTP headers of the request.                          |

---

### Response Details

| Property           | Type                    | Description                                                  |
|--------------------|-------------------------|--------------------------------------------------------------|
| `ResContentType`   | `string`               | The content type of the response.                            |
| `ResContentLength` | `long?`                | The content length of the response body (if available).      |
| `ResBody`          | `string?`             | The body of the response (if enabled).                       |
| `ResHeaders`       | `Dictionary<string, string>` | The HTTP headers of the response.                         |

---

### Exception Details

| Property       | Type        | Description                                                   |
|----------------|-------------|---------------------------------------------------------------|
| `ExData`       | `IDictionary?` | Additional data associated with the exception.            |
| `ExHelpLink`   | `string?`   | A link to documentation or support for the exception.         |
| `ExHResult`    | `int?`      | The HRESULT code for the exception.                           |
| `ExMessage`    | `string?`   | The message describing the exception.                        |
| `ExSource`     | `string?`   | The source of the exception (e.g., assembly or method name).  |
| `ExStackTrace` | `string?`   | The stack trace for the exception.                            |

---

### Usage

The `HttpLog` class provides a structured format for capturing request, response, and exception data. It is typically populated by the HTTP Logging Middleware and stored in a database or other logging infrastructure for further analysis.

This data is useful for:
- Debugging and resolving issues in the application.
- Auditing HTTP interactions for compliance or monitoring.
- Analyzing application performance and identifying bottlenecks.
