-- Table for ApiTokens
CREATE TABLE ApiTokens (
    Id UUID PRIMARY KEY, -- GUID as UUID
    Name VARCHAR(255) NOT NULL,
    Description TEXT,
    "Key" VARCHAR(255) NOT NULL,
    Secret VARCHAR(255) NOT NULL,
    ExpireAt TIMESTAMP,
    Enabled BOOLEAN NOT NULL DEFAULT TRUE, -- Boolean as BOOLEAN
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP
);

-- Table for Policies (related to ApiTokens)
CREATE TABLE ApiTokenPolicies (
    Id UUID PRIMARY KEY, -- GUID as UUID
    ApiTokenId UUID NOT NULL, -- Foreign key to ApiTokens
    Area VARCHAR(255) NOT NULL,
    Actions TEXT NOT NULL,
    CONSTRAINT FK_ApiTokenPolicies_ApiTokens FOREIGN KEY (ApiTokenId) REFERENCES ApiTokens(Id) ON DELETE CASCADE
);

CREATE INDEX IX_ApiTokenPolicies_ApiTokenId ON ApiTokenPolicies (ApiTokenId);

-- Table for GlobalSettings
CREATE TABLE GlobalSettings (
    Id UUID PRIMARY KEY,
    SuperAdmins TEXT, -- Comma-separated string for super admins
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP
);

-- Table for PluginDefinitions
CREATE TABLE PluginDefinitions (
    Id UUID PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    Category VARCHAR(255) NOT NULL,
    Assembly VARCHAR(255) NOT NULL,
    Icon VARCHAR(255), -- Nullable
    Description TEXT, -- Nullable
    Stylesheets TEXT, -- Nullable
    Locked BOOLEAN NOT NULL DEFAULT FALSE, -- Boolean as BOOLEAN
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP
);

-- Table for PluginDefinitionTypes
CREATE TABLE PluginDefinitionTypes (
    Id UUID PRIMARY KEY,
    PluginDefinitionId UUID NOT NULL, -- Foreign key to PluginDefinitions
    Name VARCHAR(255) NOT NULL,
    Type VARCHAR(255) NOT NULL,
    IsDefault BOOLEAN NOT NULL DEFAULT FALSE, -- Boolean as BOOLEAN
    CONSTRAINT FK_PluginDefinitionTypes_PluginDefinitions FOREIGN KEY (PluginDefinitionId) REFERENCES PluginDefinitions(Id) ON DELETE CASCADE
);

CREATE INDEX IX_PluginDefinitionTypes_PluginDefinitionId ON PluginDefinitionTypes (PluginDefinitionId);

-- Table for Sites
CREATE TABLE Sites (
    Id UUID PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    Description TEXT,
    Urls TEXT, -- Comma-separated list of URLs
    LayoutId UUID NOT NULL,
    DetailLayoutId UUID NOT NULL,
    EditLayoutId UUID NOT NULL,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP
);

-- Table for Settings
CREATE TABLE Settings (
    Id UUID PRIMARY KEY, -- GUID as UUID
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP
);

-- Table for SettingValues
CREATE TABLE SettingValues (
    Id UUID PRIMARY KEY,
    SettingsId UUID NOT NULL, -- Foreign key to Settings
    "Key" VARCHAR(255) NOT NULL,
    "Value" TEXT NOT NULL,
    CONSTRAINT FK_SettingValues_Settings FOREIGN KEY (SettingsId) REFERENCES Settings(Id) ON DELETE CASCADE
);

CREATE INDEX IX_SettingValues_SettingsId_Key ON SettingValues (SettingsId, "Key");

-- Table for Users
CREATE TABLE Users (
    Id UUID PRIMARY KEY,
    UserName VARCHAR(255),
    NormalizedUserName VARCHAR(255),
    Email VARCHAR(255),
    NormalizedEmail VARCHAR(255),
    EmailConfirmed BOOLEAN NOT NULL,
    PasswordHash TEXT,
    SecurityStamp VARCHAR(255),
    ConcurrencyStamp VARCHAR(255),
    PhoneNumber VARCHAR(255),
    PhoneNumberConfirmed BOOLEAN NOT NULL,
    TwoFactorEnabled BOOLEAN NOT NULL,
    LockoutEnd TIMESTAMP,
    LockoutEnabled BOOLEAN NOT NULL,
    AccessFailedCount INT NOT NULL,
    LoginAt TIMESTAMP,
    LoginCount INT NOT NULL,
    PasswordChangedAt TIMESTAMP,
    PasswordChangedBy VARCHAR(255),
    Enabled BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP,
    AuthenticatorKey VARCHAR(255),
    FirstName VARCHAR(255),
    LastName VARCHAR(255)
);

CREATE INDEX IX_Users_UserName_Email ON Users (UserName, Email);

-- Table for Roles
CREATE TABLE Roles (
    Id UUID PRIMARY KEY,
    SiteId UUID NOT NULL,
    Name VARCHAR(255) NOT NULL,
    Description TEXT,
    Type INT NOT NULL, -- Enum as INT
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP,
    CONSTRAINT FK_Roles_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Roles_SiteId_Name ON Roles (SiteId, Name);

-- Table for Folders
CREATE TABLE Folders (
    Id UUID PRIMARY KEY,
    SiteId UUID NOT NULL,
    Name VARCHAR(255) NOT NULL,
    NormalizedName VARCHAR(255) NOT NULL,
    ParentId UUID,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP,
    CONSTRAINT FK_Folders_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Folders_SiteId_Name ON Folders (SiteId, Name);

-- Table for Files
CREATE TABLE Files (
    Id UUID PRIMARY KEY,
    SiteId UUID NOT NULL,
    Name VARCHAR(255) NOT NULL,
    NormalizedName VARCHAR(255) NOT NULL,
    FolderId UUID NOT NULL,
    Extension VARCHAR(10) NOT NULL,
    ContentType VARCHAR(255) NOT NULL,
    Size BIGINT NOT NULL,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP,
    CONSTRAINT FK_Files_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Files_Folders FOREIGN KEY (FolderId) REFERENCES Folders(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Files_SiteId_Name ON Files (SiteId, Name);

-- Table for Blocks
CREATE TABLE Blocks (
    Id UUID PRIMARY KEY,
    SiteId UUID NOT NULL,
    Name VARCHAR(255) NOT NULL,
    Category VARCHAR(255) NOT NULL,
    Description TEXT,
    Content TEXT NOT NULL,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP,
    CONSTRAINT FK_Blocks_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Blocks_SiteId_Name ON Blocks (SiteId, Name);

-- Table for Pages
CREATE TABLE Pages (
    Id UUID PRIMARY KEY,
    SiteId UUID NOT NULL,
    Title VARCHAR(255) NOT NULL,
    ParentId UUID,
    "Order" INT NOT NULL,
    Path VARCHAR(255) NOT NULL,
    LayoutId UUID,
    EditLayoutId UUID,
    DetailLayoutId UUID,
    Locked BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedBy VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ModifiedBy VARCHAR(255),
    ModifiedAt TIMESTAMP,
    CONSTRAINT FK_Pages_Sites FOREIGN KEY (SiteId) REFERENCES Sites(Id) ON DELETE CASCADE
);

CREATE INDEX IX_Pages_SiteId_Path ON Pages (SiteId, Path);
