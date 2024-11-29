## Roles Definition
Roles in FluentCMS define the permissions and access levels for users within a site. Each role can have specific policies that dictate what actions users assigned to that role can perform.

## Scenarios
### Senario1: E-commerce Platform Admin and Customer Roles
#### Background:
An e-commerce platform, "ShopSmart," has different types of users including administrators and customers.
#### Requirement: 
Define roles for administrators who manage the site and customers who shop on the platform.

#### Solution:
  ###### Admin Role:
  * Permissions: Create a role with full administrative permissions to manage site content, view analytics, and handle user accounts.
  * Policies: Assign policies that grant full access to all functionalities, including read-write permissions and management of other users.
  * Auto-Assignment: Optionally, set up auto-assignment for users based on their registration or onboarding process, ensuring that new admins are granted the   
  appropriate role.
  ###### Customer Role:
  * Permissions: Create a role with limited permissions for browsing products, making purchases, and accessing personal account information.
  * Policies: Apply read-only policies to ensure that customers can view and interact with content but cannot modify site settings or access administrative features.
  * Customizable: Allow customization of the customer role to include specific permissions for different customer types, such as VIP or regular customers.

### Scenario 2: Educational Institution Staff and Student Roles
#### Background: 
"TechU" university needs roles for staff and students, each with different permissions.
#### Requirement: 
Create roles for staff with administrative access and students with limited access to academic resources.

#### Solution:
  ###### Staff Role:
  * Permissions: Define a role with permissions to manage academic content, access administrative tools, and interact with student records.
  * Policies: Apply policies that grant full access to academic management systems, including the ability to add or modify courses, view student information, and 
    manage departmental resources.
  * Auto-Assignment: Implement an auto-assignment feature where staff roles are assigned based on user position or department, ensuring proper access rights are 
    granted automatically.
  ###### Student Role:
  * Permissions: Define a role with permissions limited to accessing course materials, submitting assignments, and viewing grades.
  * Policies: Apply read-only or limited edit policies to restrict access to only academic resources and personal information without administrative privileges.
  * Customizable: Allow for different student roles based on their program or year of study, providing tailored access to resources relevant to their academic track.

## Proposal
This proposal outlines the approach for defining and managing roles in FluentCMS, allowing for flexible permissions and policies tailored to the needs of various user types within a site.

## Features
* Role Creation and Management: Create, update, and delete roles. Assign roles to users and manage permissions.
* Role Listing: Display a comprehensive list of all roles with search and paging capability managed within FluentCMS .
* Auto-Assignment: Define roles that can be automatically assigned based on specific criteria.
* System Roles: Identify and manage system roles that are predefined and critical to the system's operation.
* Custom Policies: Define and apply custom policies to roles, such as read-only access or administrative privileges.
* Audit Trail: Track role creation and modification history, including who made changes and when.

## Properties
* RoleId: Unique identifier for the role.
* SiteId: Identifier for the site to which the role belongs.
* Name: Name of the role.
* Description: Description of the role’s purpose and permissions.
* Policies: Array of policies associated with the role.
  * ReadOnly: Indicates if the role is read-only (The ReadOnly policy is used to define roles or permissions that restrict users to view-only access, preventing them from making any changes or modifications, eg., Content Reviewers ,Financial Reports and Analytics).
  * IsAdmin: Indicates if the role has administrative privileges.
  * IsSystem: Indicates if the role is a system-defined role.
