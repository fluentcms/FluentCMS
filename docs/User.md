## User Definition
The Users section of FluentCMS defines the management and structure of user accounts. This includes user profile details, authentication mechanisms, and account settings.

## Scenarios
### Scenario 1: E-commerce Platform User Management
#### Background: An e-commerce platform, "ShopSmart," has different user types, including customers, vendors, and administrators.
#### Requirement: Manage user accounts with varying levels of access and functionality.

#### Solution:

* User Registration and Authentication: Implement registration and authentication using the [AspNetUsers] table for secure identity management.
* Profile Management: Use the [dbo].[User] table to handle additional user profile information.
* Role-Based Access Control: Assign roles (e.g., Customer, Vendor, Admin) to users and manage permissions accordingly.
* Activity Tracking: Implement audit and logging to track user activities, such as product purchases and vendor sales reports.

### Scenario 2: Educational Institution Student and Faculty Accounts
#### Background: "TechU" university needs to manage accounts for students, faculty, and administrative staff.
#### Requirement: Implement role-based access to educational resources, course materials, and administrative tools.

#### Solution:

* User Registration and Authentication: Utilize the [AspNetUsers] table for secure authentication and management of user identities.
* Profile Management: Leverage the [dbo].[User] table to manage user-specific information like profile photos and contact details.
* Role-Based Access Control: Assign appropriate roles (e.g., Student, Faculty, Admin) to users and manage access to resources based on these roles.
* Course and Resource Management: Enable faculty to manage course content and students to access and submit assignments through role-specific permissions.
* Activity Monitoring: Implement audit and logging mechanisms to track user interactions with the system, such as accessing course materials and submitting assignments.

## Proposal
This proposal outlines the approach for managing user accounts within FluentCMS, focusing on user attributes, authentication, roles, and permissions to support various user types and access levels.

## Features
* User Creation and Management: Ability to create, update, and delete user accounts, assign roles, and manage user permissions.
* Profile Management: Users can update personal information, including profile photos and contact details.
* Authentication: Secure authentication methods, including password management, two-factor authentication (MFA), and session handling.
* Role Assignment: Assign and manage roles and permissions for different user types.
* Account Status: Manage account statuses such as active, inactive, suspended, or locked.
* Audit and Logging: Track user activity, login history, and account changes for security and compliance.
* Custom Application-Specific Needs: Provide the custom application-specific needs and the core identity management functionalities provided by ASP.NET Identity.
  
## Properties

* UserId: Unique identifier for the user.
* UserName: User’s login name.
* NormalizedUserName: The normalized username used for searches.
* DisplayName: User’s display name.
* Email: User’s email address.
* NormalizedEmail: The normalized email used for searches.
* EmailConfirmed: Indicates whether the email has been confirmed.
* PasswordHash: The hashed password for the user.
* SecurityStamp: A random value that changes whenever a user's credentials change (used for invalidating tokens, etc.).
* ConcurrencyStamp: A random value that changes whenever a user's identity data is persisted (used for optimistic concurrency checking).
* PhoneNumber: The user's phone number.
* PhoneNumberConfirmed: Indicates whether the phone number has been confirmed.
* TwoFactorEnabled: Indicates whether two-factor authentication is enabled for the user.
* TwoFactorCode: Code used for two-factor authentication.
* TwoFactorExpiry: Expiry date and time for the two-factor authentication code.
* LockoutEnd: The date and time when the user's lockout ends.
* LockoutEnabled: Indicates whether the user can be locked out.
* AccessFailedCount: The number of failed access attempts.
* PhotoFileId: Identifier for the user’s profile photo.
* LastLoginOn: Date and time of the user's last login.
* LastIpAddress: IP address used during the last login.
* CreatedBy: User who created the account.
* CreatedOn: Date and time when the account was created.
* ModifiedBy: User who last modified the account.
* ModifiedOn: Date and time when the account was last modified.
* DeletedBy: User who deleted the account.
* DeletedOn: Date and time when the account was deleted.
* IsDeleted: Indicates if the account is marked as deleted.
