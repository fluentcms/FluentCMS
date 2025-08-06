-- This script is a MySQL equivalent of the provided SQL Server schema.
-- Key translations:
-- SQL Server `uniqueidentifier` is mapped to `CHAR(36)`.
-- SQL Server `nvarchar(max)` is mapped to `LONGTEXT`.
-- SQL Server `nvarchar(n)` is mapped to `VARCHAR(n)`.
-- SQL Server `bit` is mapped to `TINYINT(1)`.
-- SQL Server `datetime` is mapped to `DATETIME`.
-- SQL Server `datetimeoffset` is mapped to `DATETIME(6)`.
-- Default constraints from SQL Server's `ALTER TABLE` statements have been added directly to the table definitions.

-- ----------------------------
-- Table structure for ApiTokens
-- ----------------------------
CREATE TABLE `ApiTokens` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `Name` LONGTEXT NOT NULL,
    `Description` LONGTEXT NULL,
    `Key` LONGTEXT NOT NULL,
    `Secret` LONGTEXT NOT NULL,
    `ExpireAt` DATETIME NULL,
    `Enabled` TINYINT(1) NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for ApiTokenPolicies
-- ----------------------------
CREATE TABLE `ApiTokenPolicies` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `ApiTokenId` CHAR(36) NOT NULL,
    `Area` LONGTEXT NOT NULL,
    `Actions` LONGTEXT NOT NULL,
    CONSTRAINT `FK_ApiTokenPolicies_ApiTokens` FOREIGN KEY (`ApiTokenId`) REFERENCES `ApiTokens` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_ApiTokenPolicies_ApiTokenId` ON `ApiTokenPolicies` (`ApiTokenId`);

-- ----------------------------
-- Table structure for GlobalSettings
-- ----------------------------
CREATE TABLE `GlobalSettings` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SuperAdmins` LONGTEXT NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for PluginDefinitions
-- ----------------------------
CREATE TABLE `PluginDefinitions` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `Name` LONGTEXT NOT NULL,
    `Category` LONGTEXT NOT NULL,
    `Assembly` LONGTEXT NOT NULL,
    `Icon` LONGTEXT NULL,
    `Description` LONGTEXT NULL,
    `Stylesheets` LONGTEXT NULL,
    `Locked` TINYINT(1) NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for PluginDefinitionTypes
-- ----------------------------
CREATE TABLE `PluginDefinitionTypes` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `PluginDefinitionId` CHAR(36) NOT NULL,
    `Name` LONGTEXT NOT NULL,
    `Type` LONGTEXT NOT NULL,
    `IsDefault` TINYINT(1) NOT NULL,
    CONSTRAINT `FK_PluginDefinitionTypes_PluginDefinitions` FOREIGN KEY (`PluginDefinitionId`) REFERENCES `PluginDefinitions` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_PluginDefinitionTypes_PluginDefinitionId` ON `PluginDefinitionTypes` (`PluginDefinitionId`);

-- ----------------------------
-- Table structure for Sites
-- ----------------------------
CREATE TABLE `Sites` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `Name` LONGTEXT NOT NULL,
    `Description` LONGTEXT NULL,
    `Urls` LONGTEXT NOT NULL,
    `LayoutId` CHAR(36) NOT NULL,
    `DetailLayoutId` CHAR(36) NOT NULL,
    `EditLayoutId` CHAR(36) NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for Settings
-- ----------------------------
CREATE TABLE `Settings` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for SettingValues
-- ----------------------------
CREATE TABLE `SettingValues` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SettingId` CHAR(36) NOT NULL, -- Corrected from SettingsId
    `Key` LONGTEXT NOT NULL,
    `Value` LONGTEXT NOT NULL,
    CONSTRAINT `FK_SettingValues_Settings` FOREIGN KEY (`SettingId`) REFERENCES `Settings` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_SettingValues_SettingId_Key` ON `SettingValues` (`SettingId`, `Key`(255)); -- Indexing a LONGTEXT column requires a prefix length

-- ----------------------------
-- Table structure for Users
-- ----------------------------
CREATE TABLE `Users` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `UserName` VARCHAR(256) NULL,
    `NormalizedUserName` VARCHAR(256) NULL,
    `Email` VARCHAR(256) NULL,
    `NormalizedEmail` VARCHAR(256) NULL,
    `EmailConfirmed` TINYINT(1) NOT NULL DEFAULT 0,
    `PasswordHash` LONGTEXT NULL,
    `SecurityStamp` LONGTEXT NULL,
    `ConcurrencyStamp` LONGTEXT NULL,
    `PhoneNumber` LONGTEXT NULL,
    `PhoneNumberConfirmed` TINYINT(1) NOT NULL DEFAULT 0,
    `TwoFactorEnabled` TINYINT(1) NOT NULL DEFAULT 0,
    `LockoutEnd` DATETIME(6) NULL, -- Mapped from datetimeoffset(7)
    `LockoutEnabled` TINYINT(1) NOT NULL DEFAULT 0,
    `AccessFailedCount` INT NOT NULL DEFAULT 0,
    `LoginAt` DATETIME NULL,
    `LoginCount` INT NOT NULL DEFAULT 0,
    `PasswordChangedAt` DATETIME NULL,
    `PasswordChangedBy` VARCHAR(256) NULL,
    `Enabled` TINYINT(1) NOT NULL DEFAULT 1,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL,
    `AuthenticatorKey` VARCHAR(512) NULL,
    `FirstName` VARCHAR(256) NULL,
    `LastName` VARCHAR(256) NULL,
    `Logins` LONGTEXT NULL,
    `Tokens` LONGTEXT NULL,
    `RecoveryCodes` LONGTEXT NULL,
    `Claims` LONGTEXT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_Users_UserName_Email` ON `Users` (`UserName`, `Email`);

-- ----------------------------
-- Table structure for Roles
-- ----------------------------
CREATE TABLE `Roles` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `Name` LONGTEXT NOT NULL,
    `Description` LONGTEXT NULL,
    `Type` INT NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL,
    CONSTRAINT `FK_Roles_Sites` FOREIGN KEY (`SiteId`) REFERENCES `Sites` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_Roles_SiteId` ON `Roles` (`SiteId`);

-- ----------------------------
-- Table structure for Folders
-- ----------------------------
CREATE TABLE `Folders` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `Name` LONGTEXT NOT NULL,
    `NormalizedName` LONGTEXT NOT NULL,
    `ParentId` CHAR(36) NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL,
    CONSTRAINT `FK_Folders_Sites` FOREIGN KEY (`SiteId`) REFERENCES `Sites` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_Folders_SiteId` ON `Folders` (`SiteId`);

-- ----------------------------
-- Table structure for Files
-- ----------------------------
CREATE TABLE `Files` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `Name` LONGTEXT NOT NULL,
    `NormalizedName` LONGTEXT NOT NULL,
    `FolderId` CHAR(36) NOT NULL,
    `Extension` VARCHAR(256) NOT NULL,
    `ContentType` VARCHAR(256) NOT NULL,
    `Size` BIGINT NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL,
    CONSTRAINT `FK_Files_Sites` FOREIGN KEY (`SiteId`) REFERENCES `Sites` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_Files_Folders` FOREIGN KEY (`FolderId`) REFERENCES `Folders` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_Files_SiteId` ON `Files` (`SiteId`);

-- ----------------------------
-- Table structure for Blocks
-- ----------------------------
CREATE TABLE `Blocks` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `Name` LONGTEXT NOT NULL,
    `Category` LONGTEXT NOT NULL,
    `Description` LONGTEXT NULL,
    `Content` LONGTEXT NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL,
    CONSTRAINT `FK_Blocks_Sites` FOREIGN KEY (`SiteId`) REFERENCES `Sites` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_Blocks_SiteId` ON `Blocks` (`SiteId`);

-- ----------------------------
-- Table structure for Pages
-- ----------------------------
CREATE TABLE `Pages` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `Title` LONGTEXT NOT NULL,
    `ParentId` CHAR(36) NULL,
    `Order` INT NOT NULL,
    `Path` LONGTEXT NOT NULL,
    `LayoutId` CHAR(36) NULL,
    `EditLayoutId` CHAR(36) NULL,
    `DetailLayoutId` CHAR(36) NULL,
    `Locked` TINYINT(1) NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL,
    CONSTRAINT `FK_Pages_Sites` FOREIGN KEY (`SiteId`) REFERENCES `Sites` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE INDEX `IX_Pages_SiteId` ON `Pages` (`SiteId`);

-- ----------------------------
-- Table structure for Layouts
-- ----------------------------
CREATE TABLE `Layouts` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `Name` LONGTEXT NOT NULL,
    `Body` LONGTEXT NOT NULL,
    `Head` LONGTEXT NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for Permissions
-- ----------------------------
CREATE TABLE `Permissions` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `EntityId` CHAR(36) NOT NULL,
    `EntityType` LONGTEXT NOT NULL,
    `Action` LONGTEXT NOT NULL,
    `RoleId` CHAR(36) NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for PluginContents
-- ----------------------------
CREATE TABLE `PluginContents` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `PluginId` CHAR(36) NOT NULL,
    `Data` LONGTEXT NOT NULL,
    `Type` LONGTEXT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for Plugins
-- ----------------------------
CREATE TABLE `Plugins` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `DefinitionId` CHAR(36) NOT NULL,
    `PageId` CHAR(36) NOT NULL,
    `Order` INT NOT NULL,
    `Cols` INT NOT NULL,
    `ColsMd` INT NULL,
    `ColsLg` INT NULL,
    `Section` LONGTEXT NULL,
    `Locked` TINYINT(1) NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for UserRoles
-- ----------------------------
CREATE TABLE `UserRoles` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `UserId` CHAR(36) NOT NULL,
    `RoleId` CHAR(36) NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for ContentTypes
-- ----------------------------
CREATE TABLE `ContentTypes` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `Slug` LONGTEXT NOT NULL,
    `Title` LONGTEXT NOT NULL,
    `Description` LONGTEXT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for ContentTypeFields
-- ----------------------------
CREATE TABLE `ContentTypeFields` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `ContentTypeId` CHAR(36) NOT NULL,
    `Name` LONGTEXT NOT NULL,
    `Description` LONGTEXT NOT NULL,
    `Type` LONGTEXT NOT NULL,
    `Settings` LONGTEXT NOT NULL,
    `Required` TINYINT(1) NOT NULL,
    `Unique` TINYINT(1) NOT NULL,
    `Label` LONGTEXT NULL,
    CONSTRAINT `FK_ContentTypeFields_ContentTypes` FOREIGN KEY (`ContentTypeId`) REFERENCES `ContentTypes` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------
-- Table structure for Contents
-- ----------------------------
CREATE TABLE `Contents` (
    `Id` CHAR(36) NOT NULL PRIMARY KEY,
    `SiteId` CHAR(36) NOT NULL,
    `TypeId` CHAR(36) NOT NULL,
    `Data` LONGTEXT NOT NULL,
    `CreatedBy` VARCHAR(256) NOT NULL,
    `CreatedAt` DATETIME NOT NULL,
    `ModifiedBy` VARCHAR(256) NULL,
    `ModifiedAt` DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
