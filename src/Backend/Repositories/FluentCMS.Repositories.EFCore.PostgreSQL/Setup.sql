-- Convert SQLite to PostgreSQL with case-sensitive identifiers and proper data types

-- Table: ApiTokens
CREATE TABLE IF NOT EXISTS "ApiTokens" (
    "Id" UUID PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL,
    "Description" TEXT,
    "Key" VARCHAR(255) NOT NULL,
    "Secret" VARCHAR(255) NOT NULL,
    "ExpireAt" TIMESTAMP,
    "Enabled" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: ApiTokenPolicies
CREATE TABLE IF NOT EXISTS "ApiTokenPolicies" (
    "Id" UUID PRIMARY KEY,
    "ApiTokenId" UUID NOT NULL,
    "Area" VARCHAR(255) NOT NULL,
    "Actions" TEXT NOT NULL,
    CONSTRAINT "FK_ApiTokenPolicies_ApiTokens" FOREIGN KEY ("ApiTokenId") 
        REFERENCES "ApiTokens" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_ApiTokenPolicies_ApiTokenId" ON "ApiTokenPolicies" ("ApiTokenId");

-- Table: Blocks
CREATE TABLE IF NOT EXISTS "Blocks" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "Name" VARCHAR(255) NOT NULL,
    "Category" VARCHAR(255) NOT NULL,
    "Content" TEXT NOT NULL,
    "Description" TEXT,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Contents
CREATE TABLE IF NOT EXISTS "Contents" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "TypeId" UUID NOT NULL,
    "Data" TEXT NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: ContentTypes
CREATE TABLE IF NOT EXISTS "ContentTypes" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "Slug" VARCHAR(255) NOT NULL,
    "Title" VARCHAR(255) NOT NULL,
    "Description" TEXT,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: ContentTypeFields
CREATE TABLE IF NOT EXISTS "ContentTypeFields" (
    "Id" UUID PRIMARY KEY,
    "ContentTypeId" UUID NOT NULL,
    "Name" VARCHAR(255) NOT NULL,
    "Description" TEXT NOT NULL,
    "Type" VARCHAR(255) NOT NULL,
    "Settings" TEXT NOT NULL,
    "Required" BOOLEAN NOT NULL,
    "Unique" BOOLEAN NOT NULL,
    "Label" TEXT,
    CONSTRAINT "FK_ContentTypeFields_ContentTypes" FOREIGN KEY ("ContentTypeId") 
        REFERENCES "ContentTypes" ("Id") ON DELETE CASCADE
);

-- Table: Files
CREATE TABLE IF NOT EXISTS "Files" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "Name" VARCHAR(255) NOT NULL,
    "NormalizedName" VARCHAR(255) NOT NULL,
    "FolderId" UUID NOT NULL,
    "Extension" VARCHAR(50) NOT NULL,
    "ContentType" VARCHAR(255) NOT NULL,
    "Size" BIGINT NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Folders
CREATE TABLE IF NOT EXISTS "Folders" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "Name" VARCHAR(255) NOT NULL,
    "NormalizedName" VARCHAR(255) NOT NULL,
    "ParentId" UUID,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: GlobalSettings
CREATE TABLE IF NOT EXISTS "GlobalSettings" (
    "Id" UUID PRIMARY KEY,
    "SuperAdmins" TEXT NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Layouts
CREATE TABLE IF NOT EXISTS "Layouts" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "Name" VARCHAR(255) NOT NULL,
    "Body" TEXT NOT NULL,
    "Head" TEXT NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Pages
CREATE TABLE IF NOT EXISTS "Pages" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "Title" VARCHAR(255) NOT NULL,
    "ParentId" UUID,
    "Order" INTEGER NOT NULL,
    "Path" VARCHAR(255) NOT NULL,
    "LayoutId" UUID,
    "EditLayoutId" UUID,
    "DetailLayoutId" UUID,
    "Locked" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Permissions
CREATE TABLE IF NOT EXISTS "Permissions" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "EntityId" UUID NOT NULL,
    "EntityType" VARCHAR(255) NOT NULL,
    "Action" VARCHAR(255) NOT NULL,
    "RoleId" UUID NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Plugins
CREATE TABLE IF NOT EXISTS "Plugins" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "DefinitionId" UUID NOT NULL,
    "PageId" UUID NOT NULL,
    "Order" INTEGER NOT NULL,
    "Cols" INTEGER NOT NULL,
    "ColsMd" INTEGER,
    "ColsLg" INTEGER,
    "Section" TEXT,
    "Locked" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: PluginContents
CREATE TABLE IF NOT EXISTS "PluginContents" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "PluginId" UUID NOT NULL,
    "Data" TEXT NOT NULL,
    "Type" VARCHAR(255),
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: PluginDefinitions
CREATE TABLE IF NOT EXISTS "PluginDefinitions" (
    "Id" UUID PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL,
    "Category" VARCHAR(255) NOT NULL,
    "Assembly" VARCHAR(255) NOT NULL,
    "Icon" TEXT,
    "Description" TEXT,
    "Stylesheets" TEXT,
    "Locked" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: PluginDefinitionTypes
CREATE TABLE IF NOT EXISTS "PluginDefinitionTypes" (
    "Id" UUID PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL,
    "Type" VARCHAR(255) NOT NULL,
    "IsDefault" BOOLEAN NOT NULL DEFAULT FALSE,
    "PluginDefinitionId" UUID NOT NULL,
    CONSTRAINT "FK_PluginDefinitionTypes_PluginDefinitions" FOREIGN KEY ("PluginDefinitionId") 
        REFERENCES "PluginDefinitions" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_PluginDefinitionTypes_PluginDefinitionId" 
    ON "PluginDefinitionTypes" ("PluginDefinitionId");

-- Table: Roles
CREATE TABLE IF NOT EXISTS "Roles" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "Name" VARCHAR(255) NOT NULL,
    "Description" TEXT,
    "Type" INTEGER NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Settings
CREATE TABLE IF NOT EXISTS "Settings" (
    "Id" UUID PRIMARY KEY,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: SettingValues
CREATE TABLE IF NOT EXISTS "SettingValues" (
    "Id" UUID PRIMARY KEY,
    "SettingId" UUID NOT NULL,
    "Key" VARCHAR(255) NOT NULL,
    "Value" TEXT NOT NULL,
    CONSTRAINT "FK_SettingValues_Settings" FOREIGN KEY ("SettingId") 
        REFERENCES "Settings" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_SettingValues_SettingId_Key" 
    ON "SettingValues" ("SettingId", "Key");

-- Table: Sites
CREATE TABLE IF NOT EXISTS "Sites" (
    "Id" UUID PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL,
    "Description" TEXT,
    "Urls" TEXT NOT NULL,
    "LayoutId" UUID NOT NULL,
    "DetailLayoutId" UUID NOT NULL,
    "EditLayoutId" UUID NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: UserRoles
CREATE TABLE IF NOT EXISTS "UserRoles" (
    "Id" UUID PRIMARY KEY,
    "SiteId" UUID NOT NULL,
    "UserId" UUID NOT NULL,
    "RoleId" UUID NOT NULL,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP
);

-- Table: Users
CREATE TABLE IF NOT EXISTS "Users" (
    "Id" UUID PRIMARY KEY,
    "UserName" VARCHAR(255),
    "NormalizedUserName" VARCHAR(255),
    "Email" VARCHAR(255),
    "NormalizedEmail" VARCHAR(255),
    "EmailConfirmed" BOOLEAN NOT NULL DEFAULT FALSE,
    "PasswordHash" TEXT,
    "SecurityStamp" VARCHAR(255),
    "ConcurrencyStamp" VARCHAR(255),
    "PhoneNumber" VARCHAR(255),
    "PhoneNumberConfirmed" BOOLEAN NOT NULL DEFAULT FALSE,
    "TwoFactorEnabled" BOOLEAN NOT NULL DEFAULT FALSE,
    "LockoutEnd" TIMESTAMP,
    "LockoutEnabled" BOOLEAN NOT NULL DEFAULT FALSE,
    "AccessFailedCount" INTEGER NOT NULL DEFAULT 0,
    "LoginAt" TIMESTAMP,
    "LoginCount" INTEGER NOT NULL DEFAULT 0,
    "PasswordChangedAt" TIMESTAMP,
    "PasswordChangedBy" VARCHAR(255),
    "Enabled" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedBy" VARCHAR(255) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ModifiedBy" VARCHAR(255),
    "ModifiedAt" TIMESTAMP,
    "AuthenticatorKey" TEXT,
    "FirstName" VARCHAR(255),
    "LastName" VARCHAR(255),
    "Logins" TEXT,
    "Tokens" TEXT,
    "RecoveryCodes" TEXT,
    "Claims" TEXT
);

-- Create indexes for commonly queried fields
CREATE INDEX IF NOT EXISTS "IX_Users_UserName" ON "Users" ("UserName");
CREATE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");
CREATE INDEX IF NOT EXISTS "IX_Files_FolderId" ON "Files" ("FolderId");
CREATE INDEX IF NOT EXISTS "IX_Pages_SiteId" ON "Pages" ("SiteId");
CREATE INDEX IF NOT EXISTS "IX_Roles_SiteId" ON "Roles" ("SiteId");
CREATE INDEX IF NOT EXISTS "IX_UserRoles_UserId" ON "UserRoles" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_UserRoles_RoleId" ON "UserRoles" ("RoleId");
