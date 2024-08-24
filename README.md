# FluentCMS

FluentCMS is a modern Content Management System (CMS) built on the powerful ASP.NET Core and the innovative Blazor technology. FluentCMS assists content writers in crafting content more efficiently. Designed to be fast, flexible, and user-friendly, it not only serves as a traditional content-based CMS but also excels as a headless CMS, making it perfect for a diverse range of digital applications.

FluentCMS is an open-source project, and we welcome contributions from the community. If you're interested in helping us improve FluentCMS, please read our [CONTRIBUTING.md](./CONTRIBUTING.md) guide.

## Features

- **Blazing Fast**: Built on top of Blazor components for client-side operations.
- **Extensible**: Easily extend with custom plugins and themes.
- **SEO Friendly**: Built-in SEO tools to optimize content for search engines.
- **Responsive**: Mobile-friendly out of the box.
- **Modern UI**: Sleek and intuitive dashboard for content management.
- **Headless Capabilities**: API-first design for decoupled applications.
- **Multi-Language Support**: Easily manage content in multiple languages.
- **Role-Based Access Control**: Granular control over user permissions.
- **Media Management**: Upload, organize, and manage media files.
- **Content Type Definition**: Define custom content types with various fields.
- **Content Management**: Create, edit, and manage content with ease.
- **User Management**: Manage users and roles with ease.
- **Role Management**: Create and manage roles with custom permissions.
- **Application Settings**: Configure application settings with ease.
- **Multiple Site Support**: Manage multiple sites from a single dashboard.
- **Page Management**: Create and manage pages with custom layouts.
- **Plugin Management**: Extend functionality with custom plugins.

## Supported Databases

- LiteDB
- MongoDB
- MySQL (coming soon)
- SQL Server (coming soon)
- SQLite (coming soon)

## Getting Started

### Prerequisites

- .NET SDK 8.0 or higher
- MongoDb (if you want to use MongoDB as database)

### Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/fluentcms/FluentCMS.git
   ```

2. Navigate to the project directory:

   ```bash
   cd FluentCMS/src/FluentCMS/
   ```

3. Run the application:

   ```bash
   dotnet run
   ```

4. Visit `http://localhost:5000` in your browser.

## Documentation

For more information on how to use FluentCMS, please refer to our [documentation](./docs/README.md). We are actively working on expanding our documentation to provide more detailed information.


## Contributing

We welcome contributions! If you're interested in improving FluentCMS, please read our [CONTRIBUTING.md](./CONTRIBUTING.md) guide.

## Roadmap

- [ ] Administration Dashboard
  - [x] User Management
  - [x] Role Management
  - [ ] Application Settings
  - [x] Headless CMS Features
    - [x] Media Management
    - [x] Content Type Definition
      - [x] Text Field
      - [x] TextArea Field
      - [x] Rich Text Field
      - [x] Markdown Field
      - [x] Number Field
      - [x] Date Field
      - [x] Time Field
      - [x] DateTime Field
      - [x] Boolean Field
      - [x] Select Field
      - [x] MultiSelect Field
      - [x] Radio Field
      - [x] Checkbox Field
      - [x] File Field
    - [x] Content Management
    - [x] API Tokens Management
  - CMS Features
    - [x] Site Management
    - [ ] Page Management
    - [ ] Plugin Definition Management
    - [ ] Plugin Management
    - [x] Layout Management
- [x] Users
  - [x] User Registration
  - [x] User Login / Logout
  - [x] User Profile
  - [x] User Forgot Password / Reset Password

## License

This project is licensed under the MIT License - see the [LICENSE](./LICENSE) file for details.
