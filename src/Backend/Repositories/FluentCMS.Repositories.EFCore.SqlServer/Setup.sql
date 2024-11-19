-- Table for ApiTokens
CREATE TABLE ApiTokens (
    Id UNIQUEIDENTIFIER PRIMARY KEY, -- GUID as UNIQUEIDENTIFIER
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    Key NVARCHAR(255) NOT NULL,
    Secret NVARCHAR(255) NOT NULL,
    ExpireAt DATETIME,
    Enabled BIT NOT NULL DEFAULT 1, -- Boolean as BIT
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME
);

-- Table for Policies (related to ApiTokens)
CREATE TABLE ApiTokenPolicies (
    Id UNIQUEIDENTIFIER PRIMARY KEY, -- GUID as UNIQUEIDENTIFIER
    ApiTokenId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to ApiTokens
    Area NVARCHAR(255) NOT NULL,
    Actions NVARCHAR(MAX) NOT NULL, -- Comma-separated string to store list of actions
    CONSTRAINT FK_ApiTokenPolicies_ApiTokens FOREIGN KEY (ApiTokenId) REFERENCES ApiTokens(Id) ON DELETE CASCADE
);

-- Index for ApiTokenPolicies by ApiTokenId for optimized querying
CREATE INDEX IX_ApiTokenPolicies_ApiTokenId ON ApiTokenPolicies (ApiTokenId);

-- Table for GlobalSettings
CREATE TABLE GlobalSettings (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SuperAdmins NVARCHAR(MAX), -- Comma-separated string for list of super admins
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME
);

-- Table for PluginDefinitions
CREATE TABLE PluginDefinitions (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Category NVARCHAR(255) NOT NULL,
    Assembly NVARCHAR(255) NOT NULL,
    Icon NVARCHAR(255), -- Nullable field
    Description NVARCHAR(MAX), -- Nullable field
    Locked BIT NOT NULL DEFAULT 0, -- Boolean as BIT
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME
);

-- Table for PluginDefinitionTypes (related to PluginDefinitions)
CREATE TABLE PluginDefinitionTypes (
    Id UNIQUEIDENTIFIER PRIMARY KEY, -- GUID as UNIQUEIDENTIFIER
    PluginDefinitionId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to PluginDefinitions
    Name NVARCHAR(255) NOT NULL,
    Type NVARCHAR(255) NOT NULL,
    IsDefault BIT NOT NULL DEFAULT 0, -- Boolean as BIT
    CONSTRAINT FK_PluginDefinitionTypes_PluginDefinitions FOREIGN KEY (PluginDefinitionId) REFERENCES PluginDefinitions(Id) ON DELETE CASCADE
);

-- Index for PluginDefinitionTypes by PluginDefinitionId and Name
CREATE INDEX IX_PluginDefinitionTypes_PluginDefinitionId ON PluginDefinitionTypes (PluginDefinitionId);

-- Table for Sites
CREATE TABLE Sites (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    Urls NVARCHAR(MAX), -- Comma-separated string for URLs list
    LayoutId UNIQUEIDENTIFIER NOT NULL,
    DetailLayoutId UNIQUEIDENTIFIER NOT NULL,
    EditLayoutId UNIQUEIDENTIFIER NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME
);

-- Add more tables similarly using UNIQUEIDENTIFIER for GUID and proper constraints...

-- Below are some common patterns reused in other table definitions:
-- - UNIQUEIDENTIFIER for GUID fields
-- - NVARCHAR for text fields with appropriate length
-- - CONSTRAINT for foreign keys with ON DELETE CASCADE
-- - BIT for boolean fields
-- - DATETIME for date-time fields
-- - CREATE INDEX for optimizing queries, especially on SiteId fields

-- Table for Roles
CREATE TABLE Roles (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    Type INT NOT NULL, -- Enum stored as INT
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Roles_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

-- Index for Roles by SiteId and Name
CREATE INDEX IX_Roles_SiteId_Name ON Roles (SiteId, Name);

-- Enable foreign key checks (default in SQL Server)
-- This doesn't require explicit statements like PRAGMA in SQLite.

-- Table for Settings
CREATE TABLE Settings (
    Id UNIQUEIDENTIFIER PRIMARY KEY, -- The Id of the entity this setting belongs to
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME
);

-- Table for SettingValues
CREATE TABLE SettingValues (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SettingsId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to Settings
    [Key] NVARCHAR(255) NOT NULL,
    [Value] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT FK_SettingValues_Settings FOREIGN KEY (SettingsId) REFERENCES Settings(Id) ON DELETE CASCADE
);

CREATE INDEX IX_SettingValues_SettingsId_Key ON SettingValues (SettingsId, [Key]);

-- Table for Users
CREATE TABLE Users (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    UserName NVARCHAR(255),
    NormalizedUserName NVARCHAR(255),
    Email NVARCHAR(255),
    NormalizedEmail NVARCHAR(255),
    EmailConfirmed BIT NOT NULL,
    PasswordHash NVARCHAR(MAX),
    SecurityStamp NVARCHAR(255),
    ConcurrencyStamp NVARCHAR(255),
    PhoneNumber NVARCHAR(255),
    PhoneNumberConfirmed BIT NOT NULL,
    TwoFactorEnabled BIT NOT NULL,
    LockoutEnd DATETIME,
    LockoutEnabled BIT NOT NULL,
    AccessFailedCount INT NOT NULL,
    LoginAt DATETIME,
    LoginCount INT NOT NULL,
    PasswordChangedAt DATETIME,
    PasswordChangedBy NVARCHAR(255),
    Enabled BIT NOT NULL DEFAULT 1,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    AuthenticatorKey NVARCHAR(255),
    FirstName NVARCHAR(255),
    LastName NVARCHAR(255)
);

CREATE INDEX IX_Users_UserName_Email ON Users (UserName, Email);

-- Table for UserRoles
CREATE TABLE UserRoles (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to Sites
    UserId UNIQUEIDENTIFIER NOT NULL,
    RoleId UNIQUEIDENTIFIER NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_UserRoles_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
);

CREATE INDEX IX_UserRoles_UserId_RoleId ON UserRoles (UserId, RoleId);

-- Table for Roles
CREATE TABLE Roles (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    Type INT NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Roles_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Roles_SiteId_Name ON Roles (SiteId, Name);

-- Table for Folders
CREATE TABLE Folders (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    NormalizedName NVARCHAR(255) NOT NULL,
    ParentId UNIQUEIDENTIFIER,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Folders_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Folders_SiteId_Name ON Folders (SiteId, Name);

-- Table for Files
CREATE TABLE Files (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    NormalizedName NVARCHAR(255) NOT NULL,
    FolderId UNIQUEIDENTIFIER NOT NULL,
    Extension NVARCHAR(10) NOT NULL,
    ContentType NVARCHAR(255) NOT NULL,
    Size BIGINT NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Files_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Files_Folders FOREIGN KEY (FolderId) REFERENCES Folders(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Files_SiteId_Name ON Files (SiteId, Name);

-- Table for Blocks
CREATE TABLE Blocks (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Category NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX),
    Content NVARCHAR(MAX) NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Blocks_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Blocks_SiteId_Name ON Blocks (SiteId, Name);

-- Table for Pages
CREATE TABLE Pages (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(255) NOT NULL,
    ParentId UNIQUEIDENTIFIER,
    [Order] INT NOT NULL,
    Path NVARCHAR(255) NOT NULL,
    LayoutId UNIQUEIDENTIFIER,
    EditLayoutId UNIQUEIDENTIFIER,
    DetailLayoutId UNIQUEIDENTIFIER,
    Locked BIT NOT NULL DEFAULT 0,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Pages_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Pages_SiteId_Path ON Pages (SiteId, Path);

-- Table for Layouts
CREATE TABLE Layouts (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Body NVARCHAR(MAX) NOT NULL,
    Head NVARCHAR(MAX) NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Layouts_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Layouts_SiteId_Name ON Layouts (SiteId, Name);

