# AspNetCoreUserManagement - API Endpoints Overview

## Authentication Controller (`/api/auth`)

- `POST /api/auth/login` - User login with email/password and 2FA
- `POST /api/auth/refresh` - Refresh JWT access token
- `POST /api/auth/logout` - Logout current session
- `POST /api/auth/logout-all` - Logout all sessions
- `GET /api/auth/sessions` - Get user's active sessions
- `DELETE /api/auth/sessions/{id}` - Terminate specific session
- `POST /api/auth/2fa/setup` - Setup two-factor authentication
- `POST /api/auth/2fa/verify` - Verify 2FA code
- `POST /api/auth/2fa/disable` - Disable 2FA

## User Controller (`/api/user`)

- `POST /api/user/register` - Register new user account
- `POST /api/user/change-password` - Change user password
- `POST /api/user/forgot-password` - Initiate password reset
- `POST /api/user/reset-password` - Reset password with token
- `POST /api/user/confirm-email` - Confirm email address
- `POST /api/user/resend-confirmation` - Resend email confirmation

## Admin Controller (`/api/admin`)

- `GET /api/admin/users` - List users (paginated, filtered, advanced search)
- `GET /api/admin/users/{id}` - Get user details
- `POST /api/admin/users` - Create user (admin)
- `PUT /api/admin/users/{id}` - Update user
- `DELETE /api/admin/users/{id}` - Delete user
- `POST /api/admin/users/{id}/activate` - Activate user account
- `POST /api/admin/users/{id}/deactivate` - Deactivate user account
- `POST /api/admin/users/{id}/suspend` - Suspend user account
- `POST /api/admin/users/{id}/reset-password` - Force password reset
