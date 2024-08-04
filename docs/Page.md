
## Page Definition
FluentCMS enables users to define and customize individual pages within a site using a flexible and intuitive interface. This capability allows for efficient management and personalization of pages, ensuring each page meets specific requirements and preferences.
## Scenarios
### Scenario 1: E-commerce Platform with Product Pages
#### Company Background:
"ShopSmart," an e-commerce platform, offers a wide range of products across various categories, including electronics, clothing, and home goods. Each product requires a detailed page to showcase its features, reviews, and purchasing options.
#### Requirement:
ShopSmart needs to create distinct product pages for each item, with the ability to customize content, images, and layout to enhance the user experience and drive sales.
#### Solution
FluentCMS provides a robust solution for ShopSmart's requirement to create distinct product pages with customized content, images, and layout:

* Dynamic Page Templates: Use pre-defined product page templates that can be customized to fit different product categories.
* Rich Media Support: Easily upload and integrate high-quality images and videos to showcase product features.
* Flexible Layouts: Utilize drag-and-drop functionality to create engaging product layouts with various content blocks.
* Interactive Features: Add interactive elements such as customer reviews, ratings, and call-to-action buttons to enhance user engagement and drive conversions.
* SEO Tools: Optimize product pages with meta tags, descriptions, and keywords to improve search engine visibility and attract more visitors.

### Scenario 2: News Website with Dynamic Content
#### Company  Background:
"DailyNews," a leading online news portal, publishes articles, videos, and interactive content on a wide range of topics, such as politics, sports, entertainment, and technology.
#### Requirement:
DailyNews needs to create dynamic pages for news articles that can include text, images, videos, and interactive elements. Each page should be easily updatable to reflect the latest news and events.
#### Solution
FluentCMS offers comprehensive tools for DailyNews to create and manage dynamic content pages for news articles:

* Real-Time Editing: Update news articles in real-time with live preview to see changes instantly.
* Multimedia Integration: Embed videos, images, and interactive elements seamlessly into news articles.
* Content Blocks: Use various content blocks to organize articles, such as text, media, quotes, and related links.
* SEO Optimization: Enhance article visibility with SEO-friendly URLs, meta tags, and descriptions.
* Content Scheduling: Schedule articles for future publication to ensure timely updates and continuous content flow.
* Version Control: Track changes to articles with version control to maintain content integrity and easily revert to previous versions if needed.

## Features

### Page Creation and Management
* Create, Update, Delete Pages: Users can create new pages, update existing ones, and delete pages as needed.
* Page Listing: Display a comprehensive list of all pages with search and paging capability managed within FluentCMS .
* Page Templates: Utilize pre-defined templates to quickly create consistent and visually appealing pages.
* Drag-and-Drop Interface: Use a drag-and-drop interface to easily add, move, and organize content blocks on a page.
* Plugins: Add pre-defined plugins or custom plugins to specific blocks of a page by simple drag-and-drop. For more details, refer to the [Plugin Definition](/PluginDefinition.md).
* Multi Appearance😧: users can define multiple appearance (views) for same page and switch between different view types and customize the appearance of their 
  pages according to their preferences.
* Multi Language 

* Approval Workflow: Implement an approval process where some pages need to be approved by a high-level role or a specific user before they can be published. This 
  ensures that content is reviewed and authorized by the appropriate individuals before it becomes publicly available.
### Content Customization
* Rich Text Editor: Add and format text content using a rich text editor.
* Media Integration: Upload and embed images, videos, and other media files directly into pages.
* Interactive Elements: Incorporate interactive elements such as forms, buttons, and sliders to enhance user engagement.
* Custom HTML/CSS: Add custom HTML and CSS to further personalize the look and feel of a page (FluentCMS HtmlString Plugin).
* Preview Mode: View pages in a preview mode to see how they will appear to end users before publishing.
### Navigation and SEO
* Page Hierarchy: Define a hierarchical structure for pages to organize content and improve navigation.
* SEO Optimization: Add meta tags, descriptions, and keywords to enhance search engine visibility.
* URL Management: Define and manage clean URLs for each page to improve usability and SEO.
### Security and Permissions
* Access Control: Ensure only authorized users can create, modify, or delete pages.
* Version Control: Track changes to pages with version control, allowing for easy rollback if needed.
* Data Validation: Implement robust data validation to maintain the integrity and security of page content.
### Real-Time Updates
Live Preview: Preview pages in real-time as they are being created or edited.
Dynamic Loading: Apply changes to page content in real-time without needing to refresh or restart the system.

## Proposal
This proposal outlines the approach for utilizing FluentCMS to create and manage individual pages on a shared infrastructure. This will enable efficient resource utilization, streamlined management, and personalized page customization for different needs, such as e-commerce product pages or news article pages.

## Properties
* PageId: Unique identifier for the page.
* SiteId: The identifier for the site to which the page belongs.
* Path: The path or URL segment for the page.
* Name: The name of the page.
* Title: The title of the page, displayed in the browser tab and search results.
* ThemeId: The theme applied to the page.
* LayoutId: The layout structure applied to the page.
* Icon: An icon representing the page.
* ParentId: The identifier of the parent page, if the page is part of a hierarchy.
* Order: The display order of the page in navigation.
* IsNavigation: Indicates if the page should be included in site navigation.
* Path: The path of the page.
* UserId: The identifier of the user who created or manages the page.
* IsPersonalizable: Indicates if the page can be personalized for individual users.
* Locked: Indicates if the page is locked from editing.
* IsClickable: Indicates if the page can be clicked in navigation.
* HeadContent: Custom content for the <head> section of the page's HTML.
* BodyContent: Custom content for the <body> section of the page's HTML.
* EffectiveDate: The date and time when the page becomes effective.
* ExpiryDate: The date and time when the page expires.
* DeletedBy: The user who deleted the page, if applicable.
* DeletedOn: The date and time when the page was deleted, if applicable.
* IsDeleted: Indicates if the page has been deleted.(can replaced by status property)
* Status: The status of the page with the following values: draft,waiteforConfirm ,edited, published, scheduled, deleted.
* ApprovalRequired: Indicates if the page requires approval before publishing.
* ApprovedBy: The user who approved the page, if applicable.
* ApprovalDate: The date and time when the page was approved, if applicable
* ViewTypeIds: The type of view to render for the page (e.g., "Cards", "Grid," "List,","Table","Custom" ... )
