-- Table for ApiToken
CREATE TABLE ApiToken (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    "Key" NVARCHAR(255) NOT NULL,
    Secret NVARCHAR(255) NOT NULL,
    ExpireAt DATETIME NULL,
    Enabled BIT NOT NULL DEFAULT 1,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

-- Index for fast lookup by Key and Enabled status
CREATE INDEX IX_ApiToken_Key_Enabled ON ApiToken ("Key", Enabled);

-- Table for Policy entries related to ApiToken
CREATE TABLE "Policy" (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ApiTokenId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to ApiToken
    Area NVARCHAR(255) NOT NULL,
    Actions NVARCHAR(MAX) NOT NULL, -- Comma-separated actions stored as string

    CONSTRAINT FK_Policy_ApiToken FOREIGN KEY (ApiTokenId)
        REFERENCES ApiToken(Id) ON DELETE CASCADE
);

-- Table for GlobalSettings
CREATE TABLE GlobalSettings (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Initialized BIT NOT NULL DEFAULT 0,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL,

    -- Flattened fields for FileUploadConfig with 'FileUpload_' prefix
    FileUpload_MaxSize BIGINT NOT NULL,
    FileUpload_MaxCount INT NOT NULL,
    FileUpload_AllowedExtensions NVARCHAR(255) NOT NULL,

    -- SuperAdmins stored as a comma-separated string
    SuperAdmins NVARCHAR(MAX) NULL
);


-- Table for PluginDefinition
CREATE TABLE PluginDefinition (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Category NVARCHAR(255) NOT NULL,
    Assembly NVARCHAR(255) NOT NULL,
    Icon NVARCHAR(255) NULL,
    Description NVARCHAR(MAX) NULL,
    Locked BIT NOT NULL DEFAULT 0,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

-- Table for PluginDefinitionType entries related to PluginDefinition
CREATE TABLE PluginDefinitionType (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    PluginDefinitionId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to PluginDefinition
    Name NVARCHAR(255) NOT NULL,
    Type NVARCHAR(255) NOT NULL,
    IsDefault BIT NOT NULL DEFAULT 0,

    CONSTRAINT FK_PluginDefinitionType_PluginDefinition FOREIGN KEY (PluginDefinitionId)
        REFERENCES PluginDefinition(Id) ON DELETE CASCADE
);


-- Table for Settings
CREATE TABLE Settings (
    Id UNIQUEIDENTIFIER PRIMARY KEY, -- Id is the Id of the associated entity
    "Values" NVARCHAR(MAX) NOT NULL, -- JSON representation of the dictionary
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

-- Table for User
CREATE TABLE [User] (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    UserName NVARCHAR(256) NULL,
    NormalizedUserName NVARCHAR(256) NULL,
    Email NVARCHAR(256) NULL,
    NormalizedEmail NVARCHAR(256) NULL,
    EmailConfirmed BIT NOT NULL,
    PasswordHash NVARCHAR(MAX) NULL,
    SecurityStamp NVARCHAR(MAX) NULL,
    ConcurrencyStamp NVARCHAR(MAX) NULL,
    PhoneNumber NVARCHAR(MAX) NULL,
    PhoneNumberConfirmed BIT NOT NULL,
    TwoFactorEnabled BIT NOT NULL,
    LockoutEnd DATETIMEOFFSET NULL,
    LockoutEnabled BIT NOT NULL,
    AccessFailedCount INT NOT NULL,
    LoginAt DATETIME NULL,
    LoginCount INT NOT NULL,
    PasswordChangedAt DATETIME NULL,
    PasswordChangedBy NVARCHAR(255) NULL,
    Enabled BIT NOT NULL DEFAULT 1,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL,
    AuthenticatorKey NVARCHAR(MAX) NULL,
    FirstName NVARCHAR(255) NULL,
    LastName NVARCHAR(255) NULL
);

-- Index for fast lookup by UserName and Email
CREATE INDEX IX_User_UserName_Email ON [User] (UserName, Email);

-- Table for IdentityUserLogin related to User
CREATE TABLE IdentityUserLogin (
    LoginProvider NVARCHAR(450) NOT NULL,
    ProviderKey NVARCHAR(450) NOT NULL,
    ProviderDisplayName NVARCHAR(256) NULL,
    UserId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to User

    PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_IdentityUserLogin_User FOREIGN KEY (UserId)
        REFERENCES [User](Id) ON DELETE CASCADE
);

-- Table for IdentityUserToken related to User
CREATE TABLE IdentityUserToken (
    UserId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to User
    LoginProvider NVARCHAR(450) NOT NULL,
    Name NVARCHAR(450) NOT NULL,
    Value NVARCHAR(MAX) NULL,

    PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_IdentityUserToken_User FOREIGN KEY (UserId)
        REFERENCES [User](Id) ON DELETE CASCADE
);

-- Table for IdentityUserClaim related to User
CREATE TABLE IdentityUserClaim (
    Id INT PRIMARY KEY IDENTITY,
    UserId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to User
    ClaimType NVARCHAR(MAX) NULL,
    ClaimValue NVARCHAR(MAX) NULL,

    CONSTRAINT FK_IdentityUserClaim_User FOREIGN KEY (UserId)
        REFERENCES [User](Id) ON DELETE CASCADE
);

-- Table for UserTwoFactorRecoveryCode related to User
CREATE TABLE UserTwoFactorRecoveryCode (
    Code NVARCHAR(255) PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to User
    Redeemed BIT NOT NULL DEFAULT 0,

    CONSTRAINT FK_UserTwoFactorRecoveryCode_User FOREIGN KEY (UserId)
        REFERENCES [User](Id) ON DELETE CASCADE
);

-- Index on UserId for quick access to recovery codes by User
CREATE INDEX IX_UserTwoFactorRecoveryCode_UserId ON UserTwoFactorRecoveryCode (UserId);

-- Table for Site
CREATE TABLE Site (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    Urls NVARCHAR(MAX) NULL, -- Comma-separated string to store list of URLs
    LayoutId UNIQUEIDENTIFIER NOT NULL,
    DetailLayoutId UNIQUEIDENTIFIER NOT NULL,
    EditLayoutId UNIQUEIDENTIFIER NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

-- Index for quick lookup by Name
CREATE INDEX IX_Site_Name ON Site (Name);

-- Table for Role
CREATE TABLE Role (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to Site
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    Type INT NOT NULL, -- RoleTypes enum stored as integer
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL,

    CONSTRAINT FK_Role_Site FOREIGN KEY (SiteId)
        REFERENCES Site(Id) ON DELETE CASCADE
);

-- Table for UserRole
CREATE TABLE UserRole (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to Site
    UserId UNIQUEIDENTIFIER NOT NULL,
    RoleId UNIQUEIDENTIFIER NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL,

    CONSTRAINT FK_UserRole_Site FOREIGN KEY (SiteId)
        REFERENCES Site(Id) ON DELETE CASCADE,

    CONSTRAINT FK_UserRole_Role FOREIGN KEY (RoleId)
        REFERENCES Role(Id) ON DELETE CASCADE
);

-- Table for Block
CREATE TABLE Block (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Category NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    Content NVARCHAR(MAX) NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

CREATE INDEX IX_Block_SiteId_Name ON Block (SiteId, Name);

-- Table for Content
CREATE TABLE Content (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    TypeId UNIQUEIDENTIFIER NOT NULL,
    Data NVARCHAR(MAX) NOT NULL, -- JSON representation of the dictionary
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

CREATE INDEX IX_Content_SiteId_TypeId ON Content (SiteId, TypeId);

-- Table for ContentType
CREATE TABLE ContentType (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Slug NVARCHAR(255) NOT NULL,
    Title NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

CREATE INDEX IX_ContentType_SiteId_Slug ON ContentType (SiteId, Slug);

-- Table for ContentTypeField, as child table for ContentType
CREATE TABLE ContentTypeField (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ContentTypeId UNIQUEIDENTIFIER NOT NULL, -- Foreign key to ContentType
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(MAX) NOT NULL,
    Type NVARCHAR(255) NOT NULL,
    Required BIT NOT NULL,
    Unique BIT NOT NULL,
    Label NVARCHAR(255) NOT NULL,
    Settings NVARCHAR(MAX) NULL, -- JSON representation of the dictionary
    CONSTRAINT FK_ContentTypeField_ContentType FOREIGN KEY (ContentTypeId)
        REFERENCES ContentType(Id) ON DELETE CASCADE
);

CREATE INDEX IX_ContentTypeField_ContentTypeId_Name ON ContentTypeField (ContentTypeId, Name);

-- Table for File
CREATE TABLE File (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    NormalizedName NVARCHAR(255) NOT NULL,
    FolderId UNIQUEIDENTIFIER NOT NULL,
    Extension NVARCHAR(50) NOT NULL,
    ContentType NVARCHAR(255) NOT NULL,
    Size BIGINT NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

CREATE INDEX IX_File_SiteId_Name ON File (SiteId, Name);

-- Table for Folder
CREATE TABLE Folder (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    NormalizedName NVARCHAR(255) NOT NULL,
    ParentId UNIQUEIDENTIFIER NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

CREATE INDEX IX_Folder_SiteId_Name ON Folder (SiteId, Name);

-- Table for Layout
CREATE TABLE Layout (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(255) NOT NULL,
    Body NVARCHAR(MAX) NOT NULL,
    Head NVARCHAR(MAX) NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

CREATE INDEX IX_Layout_SiteId_Name ON Layout (SiteId, Name);

-- Table for Page
CREATE TABLE Page (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    Title NVARCHAR(255) NOT NULL,
    ParentId UNIQUEIDENTIFIER NULL,
    [Order] INT NOT NULL,
    [Path] NVARCHAR(255) NOT NULL, -- URL path as a single segment
    LayoutId UNIQUEIDENTIFIER NULL,
    EditLayoutId UNIQUEIDENTIFIER NULL,
    DetailLayoutId UNIQUEIDENTIFIER NULL,
    Locked BIT NOT NULL DEFAULT 0,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

CREATE INDEX IX_Page_SiteId_Path ON Page (SiteId, [Path]);


-- Table for Permission
CREATE TABLE Permission (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    EntityId UNIQUEIDENTIFIER NOT NULL,
    EntityType NVARCHAR(255) NOT NULL,
    Action NVARCHAR(255) NOT NULL,
    RoleId UNIQUEIDENTIFIER NOT NULL,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

-- Index for fast lookup by SiteId, EntityType, and RoleId
CREATE INDEX IX_Permission_SiteId_EntityType_RoleId ON Permission (SiteId, EntityType, RoleId);

-- Table for Plugin
CREATE TABLE Plugin (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    DefinitionId UNIQUEIDENTIFIER NOT NULL,
    PageId UNIQUEIDENTIFIER NOT NULL,
    [Order] INT NOT NULL DEFAULT 0,
    Cols INT NOT NULL DEFAULT 12,
    ColsMd INT NOT NULL DEFAULT 0,
    ColsLg INT NOT NULL DEFAULT 0,
    Section NVARCHAR(255) NOT NULL,
    Locked BIT NOT NULL DEFAULT 0,
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL
);

-- Index for optimized retrieval by SiteId, DefinitionId, and PageId
CREATE INDEX IX_Plugin_SiteId_DefinitionId_PageId ON Plugin (SiteId, DefinitionId, PageId);

-- Table for PluginContent
CREATE TABLE PluginContent (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    SiteId UNIQUEIDENTIFIER NOT NULL,
    PluginId UNIQUEIDENTIFIER NOT NULL,
    Type NVARCHAR(255) NOT NULL,
    Data NVARCHAR(MAX) NULL, -- JSON representation of the dictionary
    CreatedBy NVARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ModifiedBy NVARCHAR(255) NULL,
    ModifiedAt DATETIME NULL,

    CONSTRAINT FK_PluginContent_Plugin FOREIGN KEY (PluginId)
        REFERENCES Plugin(Id) ON DELETE CASCADE
);

-- Index for fast lookup by SiteId and PluginId
CREATE INDEX IX_PluginContent_SiteId_PluginId ON PluginContent (SiteId, PluginId);
