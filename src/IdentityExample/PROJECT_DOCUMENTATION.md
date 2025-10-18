# ASP.NET Core Identity Web API Project

## Project Overview

This project implements a comprehensive ASP.NET Core Web API with JWT authentication, user management, and role-based authorization. The API provides secure authentication endpoints, user profile management, administrative controls, and role management capabilities.

## Technical Stack

- **Framework**: ASP.NET Core 9.0
- **Database**: SQLite with Entity Framework Core
- **Authentication**: JWT Bearer Tokens
- **Email**: SMTP for email confirmation and password reset
- **Architecture**: Clean API with service layer pattern
- **Object Mapping**: AutoMapper for DTO mappings

## Key Features

### Authentication & Security
- JWT token-based authentication
- Email confirmation for new registrations
- Password reset via email
- Account lockout after failed login attempts
- Real-time token validation with database tracking
- Admin token invalidation capabilities

### User Management
- User registration and profile management
- Password change functionality
- Account suspension/unsuspension
- User deletion (soft/hard delete)
- Account lockout management

### Role Management
- Create, read, update, delete roles
- Assign/remove roles to/from users
- Real-time role changes (immediate effect)
- Built-in system roles (Admin, User, Guest, etc.)

### Admin Features (SuperAdmin Only)
- View all users (simple list, no pagination/filtering)
- Manage user accounts (suspend, unlock, delete)
- Token management (view active tokens, force logout)
- SuperAdmin management (grant/revoke SuperAdmin status)

## Authentication Flow

### Headers
- **API Client Auth**: `Authorization: Bearer {api-client-token}` (out of scope)
- **User Auth**: `X-User-Token: {user-jwt-token}`

### Token Strategy
1. JWT tokens are issued upon successful login
2. Tokens are stored in database for tracking and validation
3. Each request validates JWT signature AND database existence
4. Admin can invalidate tokens by removing from database
5. Expired tokens are automatically cleaned up

## Database Models

### User Model (Extended IdentityUser)
```csharp
public class User : IdentityUser<Guid>
{
    public bool IsSuperAdmin { get; set; }
    public DateTime? LastLogin { get; set; }
    public int LoginCount { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? PasswordChangedBy { get; set; }
    public bool Suspended { get; set; }
    public string Description { get; set; }
}
```

### Role Model (Extended IdentityRole)
```csharp
public class Role : IdentityRole<Guid>
{
    public string Description { get; set; }
    public RoleTypes Type { get; set; }
}

public enum RoleTypes
{
    UserDefined = 0,
    Administrators = 1,
    Authenticated = 2,
    Guest = 3,
    AllUsers = 4
}
```

### JWT Token Tracking Model
```csharp
public class JwtToken
{
    public Guid Id { get; set; }           // Token ID (jti claim)
    public Guid UserId { get; set; }       // User who owns the token
    public DateTime IssuedAt { get; set; } // When token was issued
    public DateTime ExpiresAt { get; set; } // When token expires
    public string DeviceInfo { get; set; } // Device/browser info
    public string IpAddress { get; set; }  // IP address at login
    public bool IsRevoked { get; set; }    // Manual revocation flag
}
```

## API Endpoints

### Authentication Endpoints (AuthController)
1. `POST /api/auth/register` - Register new user
2. `POST /api/auth/login` - Login and get JWT token
3. `POST /api/auth/confirm-email` - Confirm email with token
4. `POST /api/auth/resend-confirmation` - Resend email confirmation
5. `POST /api/auth/forgot-password` - Request password reset
6. `POST /api/auth/reset-password` - Reset password with token

### User Management (UsersController)
7. `GET /api/users/profile` - Get current user profile
8. `PUT /api/users/profile` - Update current user profile
9. `POST /api/users/change-password` - Change password
10. `DELETE /api/users/account` - Delete own account

### Admin User Management (AdminController) - SuperAdmin Only
11. `GET /api/admin/users` - List all users (simple list, no pagination)
12. `GET /api/admin/users/{id}` - Get specific user details
13. `PUT /api/admin/users/{id}` - Update user details
14. `POST /api/admin/users/{id}/suspend` - Suspend user
15. `POST /api/admin/users/{id}/unsuspend` - Unsuspend user
16. `POST /api/admin/users/{id}/unlock` - Unlock locked account
17. `DELETE /api/admin/users/{id}` - Delete user

### Admin SuperAdmin Management (AdminController) - SuperAdmin Only
18. `PUT /api/admin/users/{id}/superadmin` - Grant SuperAdmin status
19. `DELETE /api/admin/users/{id}/superadmin` - Revoke SuperAdmin status

### Admin Token Management (AdminController) - SuperAdmin Only
20. `GET /api/admin/tokens/active` - List all active tokens
21. `GET /api/admin/users/{id}/tokens` - Get user's active tokens
22. `DELETE /api/admin/tokens/{tokenId}` - Invalidate specific token

### Role Management (RolesController)
23. `GET /api/roles` - List all roles
24. `POST /api/roles` - Create new role
25. `GET /api/roles/{id}` - Get role details
26. `PUT /api/roles/{id}` - Update role
27. `DELETE /api/roles/{id}` - Delete role
28. `GET /api/users/{id}/roles` - Get user's roles
29. `POST /api/users/{id}/roles` - Assign role to user
30. `DELETE /api/users/{id}/roles/{roleId}` - Remove role from user

## Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=identity.db"
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key-here",
    "Issuer": "IdentityExample",
    "Audience": "IdentityExample",
    "ExpirationMinutes": 60
  },
  "SmtpSettings": {
    "Server": "smtp.gmail.com",
    "Port": 587,
    "SenderName": "Identity API",
    "SenderEmail": "noreply@yourapp.com",
    "Username": "your-smtp-username",
    "Password": "your-smtp-password",
    "UseSsl": true
  },
  "IdentityOptions": {
    "Password": {
      "RequireDigit": true,
      "RequiredLength": 8,
      "RequireNonAlphanumeric": false,
      "RequireUppercase": true,
      "RequireLowercase": true,
      "RequiredUniqueChars": 4
    },
    "Lockout": {
      "DefaultLockoutTimeSpan": "00:15:00",
      "MaxFailedAccessAttempts": 5,
      "AllowedForNewUsers": true
    },
    "User": {
      "RequireUniqueEmail": true,
      "AllowedUserNameCharacters": "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+"
    },
    "SignIn": {
      "RequireConfirmedEmail": true,
      "RequireConfirmedPhoneNumber": false,
      "RequireConfirmedAccount": true
    }
  },
  "TokenCleanupSettings": {
    "CleanupIntervalMinutes": 60,
    "ExpiredTokenRetentionHours": 24
  }
}
```

## SuperAdmin Authorization Architecture

### Authorization Strategy
- **IsSuperAdmin Property**: Uses `User.IsSuperAdmin` boolean property for admin access control
- **Custom Attribute**: `[SuperAdminRequired]` attribute for controller/action level authorization
- **Current User Service**: Scoped service to access current user information throughout request
- **Real-time Validation**: User status validated from database on each request

### SuperAdmin Management Rules
- Only SuperAdmin users can access admin endpoints
- SuperAdmin cannot grant SuperAdmin status to themselves
- SuperAdmin can grant/revoke SuperAdmin status to/from other users
- Specific error message: "Cannot modify your own SuperAdmin status"

### Current User Service
```csharp
public interface ICurrentUser
{
    Guid? UserId { get; }
    User? User { get; }
    bool IsSuperAdmin { get; }
    bool IsAuthenticated { get; }
}
```

## Project Structure

```
IdentityExample/
├── Controllers/
│   ├── AuthController.cs
│   ├── UsersController.cs
│   ├── AdminController.cs
│   └── RolesController.cs
├── Services/
│   ├── ITokenService.cs
│   ├── TokenService.cs
│   ├── IEmailService.cs
│   ├── EmailService.cs
│   ├── IUserService.cs
│   ├── UserService.cs
│   ├── ICurrentUser.cs
│   ├── CurrentUserService.cs
│   └── TokenCleanupService.cs
├── DTOs/
│   ├── Auth/
│   ├── Users/
│   ├── Admin/
│   └── Roles/
├── Attributes/
│   └── SuperAdminRequiredAttribute.cs
├── Profiles/
│   └── AutoMapperProfile.cs
├── Middleware/
│   ├── JwtMiddleware.cs
│   └── GlobalExceptionMiddleware.cs
├── Models/
│   ├── User.cs (existing)
│   ├── Role.cs (existing)
│   └── JwtToken.cs
├── Data/
│   └── AppIdentityDbContext.cs (existing)
├── Configuration/
│   ├── JwtSettings.cs
│   ├── SmtpSettings.cs
│   └── TokenCleanupSettings.cs
└── Extensions/
    ├── ServiceCollectionExtensions.cs
    └── ApplicationBuilderExtensions.cs
```

## Task Breakdown

### Phase 1: Project Setup & Dependencies
- [ ] Add required NuGet packages
- [ ] Update project configuration
- [ ] Setup database models (no migrations needed)

### Phase 2: Core Services Implementation
- [ ] Implement JWT token service
- [ ] Implement email service
- [ ] Implement user service
- [ ] Setup token cleanup background service

### Phase 3: Authentication & Security
- [ ] Configure JWT authentication
- [ ] Implement custom JWT middleware
- [ ] Setup Identity services
- [ ] Configure authorization policies

### Phase 4: Controllers & Endpoints
- [ ] Implement AuthController
- [ ] Implement UsersController
- [ ] Implement AdminController
- [ ] Implement RolesController

### Phase 5: Middleware & Error Handling
- [ ] Global exception handling middleware
- [ ] Request/response logging
- [ ] Validation middleware

## Implementation Notes

### JWT Token Claims
```json
{
  "sub": "user-guid",
  "username": "username",
  "jti": "token-guid",
  "iat": timestamp,
  "exp": timestamp,
  "iss": "IdentityExample",
  "aud": "IdentityExample"
}
```

### Response Format
All API responses use the standardized `ApiResponse<T>` format with:
- Success/failure status
- Message and error details
- Timestamp and trace information
- Consistent error handling

### Security Considerations
- No roles stored in JWT (queried from database for real-time changes)
- Token blacklisting through database validation
- Automatic cleanup of expired tokens
- Account lockout after failed attempts
- Email confirmation required for new accounts
- SuperAdmin authorization based on User.IsSuperAdmin property
- Real-time user status validation from database

### Performance Optimizations
- Background service for token cleanup
- Efficient database queries with proper indexing
- Minimal JWT payload for better performance
- Scoped CurrentUserService (no additional caching needed)

### Development Guidelines
- **IMPORTANT**: Do not use XML documentation comments
- Use inline comments only for code documentation
- AutoMapper for all DTO mappings
- Keep DTO structure simple (not over-complicated)
- Use async methods in controllers without "Async" suffix

This documentation serves as the complete blueprint for implementing the Identity Web API project.
