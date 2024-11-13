-- Enable foreign key constraints (required in SQLite)
PRAGMA foreign_keys = ON;

-- Table for ApiTokens
CREATE TABLE ApiTokens (
    Id TEXT PRIMARY KEY, -- GUID as TEXT
    Name TEXT NOT NULL,
    Description TEXT,
    Key TEXT NOT NULL,
    Secret TEXT NOT NULL,
    ExpireAt DATETIME,
    Enabled INTEGER NOT NULL DEFAULT 1, -- Boolean as INTEGER (1 for true, 0 for false)
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table for Policies (related to ApiTokens)
CREATE TABLE Policies (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, -- Auto-incrementing ID
    ApiTokenId TEXT NOT NULL, -- Foreign key to ApiTokens
    Area TEXT NOT NULL,
    Actions TEXT NOT NULL, -- Comma-separated string to store list of actions
    FOREIGN KEY (ApiTokenId) REFERENCES ApiTokens(Id) ON DELETE CASCADE
);

-- Index for Policies by ApiTokenId for optimized querying
CREATE INDEX IX_Policies_ApiTokenId ON Policies (ApiTokenId);


-- Table for GlobalSettings
CREATE TABLE GlobalSettings (
    Id TEXT PRIMARY KEY,
    SuperAdmins TEXT, -- Comma-separated string for list of super admins
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table for PluginDefinitions
CREATE TABLE PluginDefinitions (
    Id TEXT PRIMARY KEY, -- GUID as TEXT
    Name TEXT NOT NULL,
    Category TEXT NOT NULL,
    Assembly TEXT NOT NULL,
    Icon TEXT, -- Nullable field
    Description TEXT, -- Nullable field
    Locked INTEGER NOT NULL DEFAULT 0, -- Boolean as INTEGER (1 for true, 0 for false)
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);


-- Table for PluginDefinitionTypes (related to PluginDefinitions)
CREATE TABLE PluginDefinitionTypes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT, -- Auto-incrementing ID
    PluginDefinitionId TEXT NOT NULL, -- Foreign key to PluginDefinitions
    Name TEXT NOT NULL,
    Type TEXT NOT NULL,
    IsDefault INTEGER NOT NULL DEFAULT 0, -- Boolean as INTEGER (1 for true, 0 for false)
    FOREIGN KEY (PluginDefinitionId) REFERENCES PluginDefinitions(Id) ON DELETE CASCADE
);

-- Index for PluginDefinitionTypes by PluginDefinitionId and Name
CREATE INDEX IX_PluginDefinitionTypes_PluginDefinitionId ON PluginDefinitionTypes (PluginDefinitionId);


-- Table for Sites
CREATE TABLE Sites (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Urls TEXT, -- Comma-separated string for URLs list
    LayoutId TEXT NOT NULL,
    DetailLayoutId TEXT NOT NULL,
    EditLayoutId TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table for Settings
CREATE TABLE Settings (
    Id TEXT PRIMARY KEY, -- The Id of the entity this setting belongs to
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table for SettingValues (child table of Settings)
CREATE TABLE SettingValues (
    Id TEXT PRIMARY KEY,
    SettingsId TEXT NOT NULL, -- Foreign key to Settings table
    Key TEXT NOT NULL,
    Value TEXT NOT NULL,
    FOREIGN KEY (SettingsId) REFERENCES Settings(Id) ON DELETE CASCADE
);

-- Index for efficient lookup by SettingsId and Key
CREATE INDEX IX_SettingValues_SettingsId_Key ON SettingValues (SettingsId, Key);

-- Table for Roles
CREATE TABLE Roles (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL, -- Foreign key to Sites
    Name TEXT NOT NULL,
    Description TEXT,
    Type INTEGER NOT NULL, -- Enum stored as INTEGER
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME,
    FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

-- Index for Roles by SiteId and Name
CREATE INDEX IX_Roles_SiteId_Name ON Roles (SiteId, Name);

-- Table for UserRoles (junction table for Users and Roles)
CREATE TABLE UserRoles (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL, -- Foreign key to Sites
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL, -- Foreign key to Roles
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME,
    FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE,
    FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
);

-- Index for UserRoles by UserId and RoleId
CREATE INDEX IX_UserRoles_UserId_RoleId ON UserRoles (UserId, RoleId);

-- Table for Users
CREATE TABLE Users (
    Id TEXT PRIMARY KEY,
    UserName TEXT,
    NormalizedUserName TEXT,
    Email TEXT,
    NormalizedEmail TEXT,
    EmailConfirmed INTEGER NOT NULL, -- Boolean as INTEGER
    PasswordHash TEXT,
    SecurityStamp TEXT,
    ConcurrencyStamp TEXT,
    PhoneNumber TEXT,
    PhoneNumberConfirmed INTEGER NOT NULL,
    TwoFactorEnabled INTEGER NOT NULL,
    LockoutEnd DATETIME,
    LockoutEnabled INTEGER NOT NULL,
    AccessFailedCount INTEGER NOT NULL,
    LoginAt DATETIME,
    LoginCount INTEGER NOT NULL,
    PasswordChangedAt DATETIME,
    PasswordChangedBy TEXT,
    Enabled INTEGER NOT NULL DEFAULT 1, -- Boolean as INTEGER
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME,
    AuthenticatorKey TEXT,
    FirstName TEXT,
    LastName TEXT
);

-- Index for Users by UserName and Email
CREATE INDEX IX_Users_UserName_Email ON Users (UserName, Email);

-- Table for UserLogins (related to Users)
CREATE TABLE UserLogins (
    LoginProvider TEXT NOT NULL,
    ProviderKey TEXT NOT NULL,
    ProviderDisplayName TEXT,
    UserId TEXT NOT NULL, -- Foreign key to Users
    PRIMARY KEY (LoginProvider, ProviderKey),
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- Table for UserTokens (related to Users)
CREATE TABLE UserTokens (
    UserId TEXT NOT NULL, -- Foreign key to Users
    LoginProvider TEXT NOT NULL,
    Name TEXT NOT NULL,
    Value TEXT,
    PRIMARY KEY (UserId, LoginProvider, Name),
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- Table for UserClaims (related to Users)
CREATE TABLE UserClaims (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL, -- Foreign key to Users
    ClaimType TEXT,
    ClaimValue TEXT,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- Table for UserTwoFactorRecoveryCodes (related to Users)
CREATE TABLE UserTwoFactorRecoveryCodes (
    Code TEXT PRIMARY KEY,
    UserId TEXT NOT NULL, -- Foreign key to Users
    Redeemed INTEGER NOT NULL DEFAULT 0, -- Boolean as INTEGER
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

-- Table for Blocks
CREATE TABLE Blocks (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL, -- Foreign key to Sites
    Name TEXT NOT NULL,
    Category TEXT NOT NULL,
    Description TEXT,
    Content TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Blocks by SiteId and Name
CREATE INDEX IX_Blocks_SiteId_Name ON Blocks (SiteId, Name);

-- Table for Contents
CREATE TABLE Contents (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL, -- Foreign key to Sites
    TypeId TEXT NOT NULL,
    Data TEXT, -- JSON representation of the dictionary
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Contents by SiteId and TypeId
CREATE INDEX IX_Contents_SiteId_TypeId ON Contents (SiteId, TypeId);

-- Table for ContentTypes
CREATE TABLE ContentTypes (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Slug TEXT NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for ContentTypes by SiteId and Slug
CREATE INDEX IX_ContentTypes_SiteId_Slug ON ContentTypes (SiteId, Slug);

-- Table for ContentTypeFields (related to ContentTypes)
CREATE TABLE ContentTypeFields (
    Id TEXT PRIMARY KEY,
    ContentTypeId TEXT NOT NULL, -- Foreign key to ContentTypes
    Name TEXT NOT NULL,
    Description TEXT NOT NULL,
    Type TEXT NOT NULL,
    "Required" INTEGER NOT NULL,
    "Unique" INTEGER NOT NULL,
    Label TEXT NOT NULL,
    Settings TEXT, -- JSON representation of the dictionary
    FOREIGN KEY (ContentTypeId) REFERENCES ContentTypes(Id) ON DELETE CASCADE
);

-- Index for ContentTypeFields by ContentTypeId and Name
CREATE INDEX IX_ContentTypeFields_ContentTypeId_Name ON ContentTypeFields (ContentTypeId, Name);

-- Table for Files
CREATE TABLE Files (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    FolderId TEXT NOT NULL,
    Extension TEXT NOT NULL,
    ContentType TEXT NOT NULL,
    Size INTEGER NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Files by SiteId and Name
CREATE INDEX IX_Files_SiteId_Name ON Files (SiteId, Name);

-- Table for Folders
CREATE TABLE Folders (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    ParentId TEXT,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Folders by SiteId and Name
CREATE INDEX IX_Folders_SiteId_Name ON Folders (SiteId, Name);

-- Table for Layouts
CREATE TABLE Layouts (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Body TEXT NOT NULL,
    Head TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Layouts by SiteId and Name
CREATE INDEX IX_Layouts_SiteId_Name ON Layouts (SiteId, Name);

-- Table for Pages
CREATE TABLE Pages (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Title TEXT NOT NULL,
    ParentId TEXT,
    "Order" INTEGER NOT NULL,
    Path TEXT NOT NULL,
    LayoutId TEXT,
    EditLayoutId TEXT,
    DetailLayoutId TEXT,
    Locked INTEGER NOT NULL DEFAULT 0, -- Boolean as INTEGER
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Pages by SiteId and Path
CREATE INDEX IX_Pages_SiteId_Path ON Pages (SiteId, Path);

-- Table for Permissions
CREATE TABLE Permissions (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    EntityId TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    Action TEXT NOT NULL,
    RoleId TEXT NOT NULL, -- Foreign key to Roles
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Permissions by SiteId, EntityType, and RoleId
CREATE INDEX IX_Permissions_SiteId_EntityType_RoleId ON Permissions (SiteId, EntityType, RoleId);

-- Table for Plugins
CREATE TABLE Plugins (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    DefinitionId TEXT NOT NULL,
    PageId TEXT NOT NULL,
    "Order" INTEGER NOT NULL DEFAULT 0,
    Cols INTEGER NOT NULL DEFAULT 12,
    ColsMd INTEGER NOT NULL DEFAULT 0,
    ColsLg INTEGER NOT NULL DEFAULT 0,
    Section TEXT NOT NULL,
    Locked INTEGER NOT NULL DEFAULT 0, -- Boolean as INTEGER
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Index for Plugins by SiteId, DefinitionId, and PageId
CREATE INDEX IX_Plugins_SiteId_DefinitionId_PageId ON Plugins (SiteId, DefinitionId, PageId);

-- Table for PluginContents (related to Plugins)
CREATE TABLE PluginContents (
    Id TEXT PRIMARY KEY,
    SiteId TEXT NOT NULL,
    PluginId TEXT NOT NULL, -- Foreign key to Plugins
    Type TEXT,
    Data TEXT, -- JSON representation of the dictionary
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy TEXT,
    ModifiedAt DATETIME,
    FOREIGN KEY (PluginId) REFERENCES Plugins(Id) ON DELETE CASCADE
);

-- Index for PluginContents by SiteId and PluginId
CREATE INDEX IX_PluginContents_SiteId_PluginId ON PluginContents (SiteId, PluginId);
