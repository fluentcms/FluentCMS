-- Table for ApiTokens
CREATE TABLE ApiTokens (
    Id CHAR(36) PRIMARY KEY, -- GUID as CHAR(36)
    Name VARCHAR(255) NOT NULL,
    Description TEXT,
    `Key` VARCHAR(255) NOT NULL,
    Secret VARCHAR(255) NOT NULL,
    ExpireAt DATETIME,
    Enabled TINYINT(1) NOT NULL DEFAULT 1, -- Boolean as TINYINT(1)
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME
) ENGINE=InnoDB;

-- Table for Policies (related to ApiTokens)
CREATE TABLE ApiTokenPolicies (
    Id CHAR(36) PRIMARY KEY, -- GUID as CHAR(36)
    ApiTokenId CHAR(36) NOT NULL, -- Foreign key to ApiTokens
    Area VARCHAR(255) NOT NULL,
    Actions TEXT NOT NULL,
    CONSTRAINT FK_ApiTokenPolicies_ApiTokens FOREIGN KEY (ApiTokenId) REFERENCES ApiTokens(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_ApiTokenPolicies_ApiTokenId ON ApiTokenPolicies (ApiTokenId);

-- Table for GlobalSettings
CREATE TABLE GlobalSettings (
    Id CHAR(36) PRIMARY KEY,
    SuperAdmins TEXT, -- Comma-separated string for super admins
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME
) ENGINE=InnoDB;

-- Table for PluginDefinitions
CREATE TABLE PluginDefinitions (
    Id CHAR(36) PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    Category VARCHAR(255) NOT NULL,
    Assembly VARCHAR(255) NOT NULL,
    Icon VARCHAR(255), -- Nullable
    Description TEXT, -- Nullable
    Stylesheets TEXT, -- Nullable
    Locked TINYINT(1) NOT NULL DEFAULT 0, -- Boolean as TINYINT(1)
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME
) ENGINE=InnoDB;

-- Table for PluginDefinitionTypes
CREATE TABLE PluginDefinitionTypes (
    Id CHAR(36) PRIMARY KEY,
    PluginDefinitionId CHAR(36) NOT NULL, -- Foreign key to PluginDefinitions
    Name VARCHAR(255) NOT NULL,
    Type VARCHAR(255) NOT NULL,
    IsDefault TINYINT(1) NOT NULL DEFAULT 0, -- Boolean as TINYINT(1)
    CONSTRAINT FK_PluginDefinitionTypes_PluginDefinitions FOREIGN KEY (PluginDefinitionId) REFERENCES PluginDefinitions(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_PluginDefinitionTypes_PluginDefinitionId ON PluginDefinitionTypes (PluginDefinitionId);

-- Table for Sites
CREATE TABLE Sites (
    Id CHAR(36) PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    Description TEXT,
    Urls TEXT, -- Comma-separated list of URLs
    LayoutId CHAR(36) NOT NULL,
    DetailLayoutId CHAR(36) NOT NULL,
    EditLayoutId CHAR(36) NOT NULL,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME
) ENGINE=InnoDB;

-- Table for Settings
CREATE TABLE Settings (
    Id CHAR(36) PRIMARY KEY, -- GUID as CHAR(36)
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME
) ENGINE=InnoDB;

-- Table for SettingValues
CREATE TABLE SettingValues (
    Id CHAR(36) PRIMARY KEY,
    SettingsId CHAR(36) NOT NULL, -- Foreign key to Settings
    `Key` VARCHAR(255) NOT NULL,
    `Value` TEXT NOT NULL,
    CONSTRAINT FK_SettingValues_Settings FOREIGN KEY (SettingsId) REFERENCES Settings(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_SettingValues_SettingsId_Key ON SettingValues (SettingsId, `Key`);

-- Table for Users
CREATE TABLE Users (
    Id CHAR(36) PRIMARY KEY,
    UserName VARCHAR(255),
    NormalizedUserName VARCHAR(255),
    Email VARCHAR(255),
    NormalizedEmail VARCHAR(255),
    EmailConfirmed TINYINT(1) NOT NULL,
    PasswordHash TEXT,
    SecurityStamp VARCHAR(255),
    ConcurrencyStamp VARCHAR(255),
    PhoneNumber VARCHAR(255),
    PhoneNumberConfirmed TINYINT(1) NOT NULL,
    TwoFactorEnabled TINYINT(1) NOT NULL,
    LockoutEnd DATETIME,
    LockoutEnabled TINYINT(1) NOT NULL,
    AccessFailedCount INT NOT NULL,
    LoginAt DATETIME,
    LoginCount INT NOT NULL,
    PasswordChangedAt DATETIME,
    PasswordChangedBy VARCHAR(255),
    Enabled TINYINT(1) NOT NULL DEFAULT 1,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME,
    AuthenticatorKey VARCHAR(255),
    FirstName VARCHAR(255),
    LastName VARCHAR(255)
) ENGINE=InnoDB;

CREATE INDEX IX_Users_UserName_Email ON Users (UserName, Email);

-- Table for Roles
CREATE TABLE Roles (
    Id CHAR(36) PRIMARY KEY,
    SiteId CHAR(36) NOT NULL,
    Name VARCHAR(255) NOT NULL,
    Description TEXT,
    Type INT NOT NULL, -- Enum as INT
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Roles_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_Roles_SiteId_Name ON Roles (SiteId, Name);

-- Table for Folders
CREATE TABLE Folders (
    Id CHAR(36) PRIMARY KEY,
    SiteId CHAR(36) NOT NULL,
    Name VARCHAR(255) NOT NULL,
    NormalizedName VARCHAR(255) NOT NULL,
    ParentId CHAR(36),
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Folders_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_Folders_SiteId_Name ON Folders (SiteId, Name);

-- Table for Files
CREATE TABLE Files (
    Id CHAR(36) PRIMARY KEY,
    SiteId CHAR(36) NOT NULL,
    Name VARCHAR(255) NOT NULL,
    NormalizedName VARCHAR(255) NOT NULL,
    FolderId CHAR(36) NOT NULL,
    Extension VARCHAR(10) NOT NULL,
    ContentType VARCHAR(255) NOT NULL,
    Size BIGINT NOT NULL,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Files_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Files_Folders FOREIGN KEY (FolderId) REFERENCES Folders(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_Files_SiteId_Name ON Files (SiteId, Name);

-- Table for Blocks
CREATE TABLE Blocks (
    Id CHAR(36) PRIMARY KEY,
    SiteId CHAR(36) NOT NULL,
    Name VARCHAR(255) NOT NULL,
    Category VARCHAR(255) NOT NULL,
    Description TEXT,
    Content TEXT NOT NULL,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Blocks_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_Blocks_SiteId_Name ON Blocks (SiteId, Name);

-- Table for Pages
CREATE TABLE Pages (
    Id CHAR(36) PRIMARY KEY,
    SiteId CHAR(36) NOT NULL,
    Title VARCHAR(255) NOT NULL,
    ParentId CHAR(36),
    `Order` INT NOT NULL,
    Path VARCHAR(255) NOT NULL,
    LayoutId CHAR(36),
    EditLayoutId CHAR(36),
    DetailLayoutId CHAR(36),
    Locked TINYINT(1) NOT NULL DEFAULT 0,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt DATETIME,
    CONSTRAINT FK_Pages_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE INDEX IX_Pages_SiteId_Path ON Pages (SiteId, Path);
