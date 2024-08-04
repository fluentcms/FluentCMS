
## Site Definition
FluentCMS empowers users to define and fully customize multiple sites using a shared infrastructure. This robust capability allows for efficient content management and personalization of various sites, ensuring each site meets unique requirements and preferences. 

## Scenarios
### Senario1: Multinational Corporation with Regional Websites
#### Company Background:
A multinational corporation, "GlobalTech," operates in multiple countries and regions worldwide. Each region has unique marketing strategies, product offerings, and regulatory requirements.
#### Requirement:
GlobalTech needs to create distinct websites for each region while maintaining a unified global brand identity. These regional websites must be managed from a central CMS, ensuring consistency and efficient resource usage.

### Scenario 2: Educational Institution with Multiple Departments and Programs
#### Institution Background:
A large university, "TechU," offers a wide range of programs across various departments and schools, such as Engineering, Business, Arts, and Sciences. Each department needs its own website to showcase its programs, faculty, and events.
#### Requirement:
TechU needs to create separate websites for each department while leveraging a shared CMS infrastructure to ensure uniformity and streamline administrative tasks.

## Features

### Multi-Site Definition
* Create, Update, Delete Sites: Users can create new sites, update existing ones, and delete sites as needed.
* Site Listing: Display a comprehensive list of all sites managed within FluentCMS.
* Create Multiple Sites: Users can create multiple independent sites within a single FluentCMS instance.
* Shared Infrastructure: Utilize a common infrastructure to support all sites, optimizing resource use and maintenance.
* Unique Site IDs: Each site is assigned a unique identifier to differentiate and manage them effectively.
* Theming and Layouts: Customize the look and feel of each site with different themes and layouts.
* Logos and Favicons: Upload distinct logos and favicons for each site to maintain brand identity.
* Home Page Settings: Set and customize the home page for each site individually.
* Head and Body Content: Add custom content to the <head> and <body> sections of each site's HTML for specific needs.
* Site Maps and URLs: Define unique site maps and URL structures to organize content and navigation effectively.
* Localized Content: Tailor content to meet the specific needs and preferences of different audiences (e.g., language localization, regional news).

### Admin Panel Integration
* Site Management Interface: Use the admin panel to easily create, update, and delete site settings.
* File Upload Capability: Upload files for site-specific assets like logos and favicons directly through the admin panel.
* Dynamic Loading: Apply changes to site settings in real-time without needing to restart the system.

### Security and Authentication
* Access Control: Ensure only authorized users can create, modify, or delete site settings.
* Data Validation: Implement robust data validation to maintain the integrity and security of site settings.

### Proposal
This proposal outlines the approach for utilizing FluentCMS to create and manage multiple distinct sites on a shared infrastructure. This will enable efficient resource utilization, streamlined management, and personalized site customization for different needs, such as a multinational corporation's regional websites or an educational institution's departmental sites.

## Properties
* Id: Unique identifier for the site.
* Name: The name of the site.
* Logo: The logo of the site.
* Favicon: The favicon of the site.
* DefaultTheme: The default theme used by the site.
* DefaultLayout: The default layout used by the site.
* DefaultHomePage: The home/default page of the site.
* SiteMap: Structure of the site, including pages and navigation.
* HeadContent: Custom content for the head section of the site’s HTML.
* BodyContent: Custom content for the body section of the site’s HTML.
* Urls: The URLs associated with the site (https://example.com, https://www.example.com, https://mysite.com/xyz).
* Version: The current version of the site settings.
* Description
* AdminRoleIds
* ViewRoleIds

## Permissions
* EntityId
* EntityType (Site, Page, Plugin, etc.)
* AccessLevel: enum (View, Manage, Admin, etc.)
* RoleId

public class Role : SiteAssociatedEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public RoleType Type { get; set; } = RoleType.Default;
}

public enum RoleType
{
    Default = 0, // user defined roles
    Administrators = 1, // system defined role for administrators
    Authenticated = 2, // system defined role for authenticated users (logged in users)
    Guest = 3, // system defined role for unauthenticated users (guests)
    All = 4 // system defined role for all users including guests and authenticated users
}

