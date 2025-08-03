-- Table: ApiTokenPolicies
CREATE TABLE ApiTokenPolicies (
    Id TEXT NOT NULL PRIMARY KEY,
    ApiTokenId TEXT NOT NULL,
    Area TEXT NOT NULL,
    Actions TEXT NOT NULL,
    FOREIGN KEY (ApiTokenId) REFERENCES ApiTokens (Id) ON DELETE CASCADE
);

-- Table: ApiTokens
CREATE TABLE ApiTokens (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Key TEXT NOT NULL,
    Secret TEXT NOT NULL,
    ExpireAt DATETIME,
    Enabled INTEGER NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Blocks
CREATE TABLE Blocks (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Category TEXT NOT NULL,
    Content TEXT NOT NULL,
    Description TEXT,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Contents
CREATE TABLE Contents (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    TypeId TEXT NOT NULL,
    Data TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: ContentTypeFields
CREATE TABLE ContentTypeFields (
    Id TEXT NOT NULL PRIMARY KEY,
    ContentTypeId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Description TEXT NOT NULL,
    Type TEXT NOT NULL,
    Settings TEXT NOT NULL,
    Required INTEGER NOT NULL,
    "Unique" INTEGER NOT NULL,
    Label TEXT,
    FOREIGN KEY (ContentTypeId) REFERENCES ContentTypes (Id) ON DELETE CASCADE
);

-- Table: ContentTypes
CREATE TABLE ContentTypes (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Slug TEXT NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Files
CREATE TABLE Files (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    FolderId TEXT NOT NULL,
    Extension TEXT NOT NULL,
    ContentType TEXT NOT NULL,
    Size INTEGER NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Folders
CREATE TABLE Folders (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    ParentId TEXT,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: GlobalSettings
CREATE TABLE GlobalSettings (
    Id TEXT NOT NULL PRIMARY KEY,
    SuperAdmins TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Layouts
CREATE TABLE Layouts (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Body TEXT NOT NULL,
    Head TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Pages
CREATE TABLE Pages (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Title TEXT NOT NULL,
    ParentId TEXT,
    `Order` INTEGER NOT NULL,
    Path TEXT NOT NULL,
    LayoutId TEXT,
    EditLayoutId TEXT,
    DetailLayoutId TEXT,
    Locked INTEGER NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Permissions
CREATE TABLE Permissions (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    EntityId TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    Action TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Plugins
CREATE TABLE Plugins (
    Id TEXT NOT NULL PRIMARY KEY,                      
    SiteId TEXT NOT NULL,                              
    DefinitionId TEXT NOT NULL,                        
    PageId TEXT NOT NULL,                              
    "Order" INTEGER NOT NULL,                          
    Cols INTEGER NOT NULL,                             
    ColsMd INTEGER,                                    
    ColsLg INTEGER,                                    
    Section TEXT,                                      
    Locked INTEGER NOT NULL DEFAULT 0,                 
    CreatedBy TEXT NOT NULL,                           
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);


-- Table: PluginContents
CREATE TABLE PluginContents (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    PluginId TEXT NOT NULL,
    Data TEXT NOT NULL,
    Type TEXT,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: PluginDefinitions
CREATE TABLE PluginDefinitions (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    Category TEXT NOT NULL,
    Assembly TEXT NOT NULL,
    Icon TEXT,
    Description TEXT,
    Stylesheets TEXT,
    Locked INTEGER NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: PluginDefinitionTypes
CREATE TABLE PluginDefinitionTypes (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    Type TEXT NOT NULL,
    IsDefault INTEGER NOT NULL,
    PluginDefinitionId TEXT NOT NULL,
    FOREIGN KEY (PluginDefinitionId) REFERENCES PluginDefinitions (Id) ON DELETE CASCADE
);

-- Table: Roles
CREATE TABLE Roles (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    Name TEXT NOT NULL,
    Description TEXT,
    Type INTEGER NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Settings
CREATE TABLE Settings (
    Id TEXT NOT NULL PRIMARY KEY,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: SettingValues
CREATE TABLE SettingValues (
    Id TEXT NOT NULL PRIMARY KEY,
    SettingId TEXT NOT NULL,
    Key TEXT NOT NULL,
    Value TEXT NOT NULL,
    FOREIGN KEY (SettingId) REFERENCES Settings (Id) ON DELETE CASCADE
);

-- Table: Sites
CREATE TABLE Sites (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    Urls TEXT NOT NULL,
    LayoutId TEXT NOT NULL,
    DetailLayoutId TEXT NOT NULL,
    EditLayoutId TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: UserRoles
CREATE TABLE UserRoles (
    Id TEXT NOT NULL PRIMARY KEY,
    SiteId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME
);

-- Table: Users
CREATE TABLE Users (
    Id TEXT NOT NULL PRIMARY KEY,
    UserName TEXT,
    NormalizedUserName TEXT,
    Email TEXT,
    NormalizedEmail TEXT,
    EmailConfirmed INTEGER NOT NULL DEFAULT 0,
    PasswordHash TEXT,
    SecurityStamp TEXT,
    ConcurrencyStamp TEXT,
    PhoneNumber TEXT,
    PhoneNumberConfirmed INTEGER NOT NULL DEFAULT 0,
    TwoFactorEnabled INTEGER NOT NULL DEFAULT 0,
    LockoutEnd DATETIME,
    LockoutEnabled INTEGER NOT NULL DEFAULT 0,
    AccessFailedCount INTEGER NOT NULL DEFAULT 0,
    LoginAt DATETIME,
    LoginCount INTEGER NOT NULL DEFAULT 0,
    PasswordChangedAt DATETIME,
    PasswordChangedBy TEXT,
    Enabled INTEGER NOT NULL DEFAULT 1,
    CreatedBy TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ModifiedBy TEXT,
    ModifiedAt DATETIME,
    AuthenticatorKey TEXT,
    FirstName TEXT,
    LastName TEXT,
    Logins TEXT,
    Tokens TEXT,
    RecoveryCodes TEXT,
    Claims TEXT
);
