# Role Management API Endpoints

## Role Controller (`/api/admin/roles`)

- `GET /api/admin/roles` - List all roles with pagination, filtering, and ordering
- `GET /api/admin/roles/{id}` - Get specific role details by ID
- `POST /api/admin/roles` - Create new role
- `PUT /api/admin/roles/{id}` - Update existing role
- `DELETE /api/admin/roles/{id}` - Delete role 
- `GET /api/admin/roles/{id}/users` - Get users assigned to a specific role

## User-Role Management (`/api/admin/users/{userId}/roles`)

- `GET /api/admin/users/{userId}/roles` - Get all roles assigned to a user
- `POST /api/admin/users/{userId}/roles` - Assign role(s) to user
- `DELETE /api/admin/users/{userId}/roles/{roleId}` - Remove specific role from user
- `DELETE /api/admin/users/{userId}/roles` - Remove all roles from user
- `PUT /api/admin/users/{userId}/roles` - Replace user's roles (bulk update)
