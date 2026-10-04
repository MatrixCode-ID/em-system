/*
    010 - Tabel inti Em: identitas user, role dan claim, sesi, log, meta, kontak, dan robot.

    Jalankan pada database target (USE [NamaDatabase]) sesudah sets/000-ulid.sql, sebelum
    berkas tables/ bernomor lebih besar. Bagian 1 ditujukan untuk database baru (CREATE TABLE
    tanpa pemeriksaan); bagian Robot dan view aman dijalankan ulang.

    Isi:
    1. Tabel inti (User, UserClaim, UserCredential, UserRole, UserSession, SystemSession, Role,
       RoleClaim, Log, Meta, Contact, Address, Comm) beserta unique, index, primary key, dan
       foreign key-nya.
    2. ta_Robot - identitas robot bersama (bergantung pada ta_User).
    3. View vi_Address, vi_Comm, vi_Contact, vi_Role, vi_User, vi_UserCredential.
*/

-- ============================================================================
-- 1. Tabel inti
-- ============================================================================

-- ----------------------------
-- Table structure for ta_Address
-- ----------------------------
CREATE TABLE [dbo].[ta_Address] (
  [cAddressId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cContactId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cAddressName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cAddressState] int  NOT NULL,
  [cAddressLocation] varchar(1000) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cAddressZip] varchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_Address] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Comm
-- ----------------------------
CREATE TABLE [dbo].[ta_Comm] (
  [cCommId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cContactId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cCommType] int  NOT NULL,
  [cCommState] int  NOT NULL,
  [cCommValue] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cCommNote] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_Comm] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Contact
-- ----------------------------
CREATE TABLE [dbo].[ta_Contact] (
  [cContactId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cContactFullName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cContactState] int  NOT NULL,
  [cContactType] int  NOT NULL,
  [cContactNote] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cContactDefaultAddress_cAddressId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cContactDefaultComm_cCommId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_Contact] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Log
-- ----------------------------
CREATE TABLE [dbo].[ta_Log] (
  [cLogId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT [dbo].[NewUlidString]() NOT NULL,
  [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cLogModule] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cLogAction] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cLogEntity] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cLogEntityId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cLogSource] varchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cLogName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cLogRefNumber] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cLogStage] int DEFAULT (0) NOT NULL,
  [cLogOrder] int DEFAULT (-1) NOT NULL,
  [cLogRevision] int DEFAULT (1) NOT NULL,
  [cLogDate] date  NOT NULL,
  [cLogNote] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_Log] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Meta
-- ----------------------------
CREATE TABLE [dbo].[ta_Meta] (
  [cMetaKey] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cMetaValue] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cMetaDescription] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [ustamp] datetime  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_Meta] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Role
-- ----------------------------
CREATE TABLE [dbo].[ta_Role] (
  [cRoleId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cRoleName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cRoleState] int  NOT NULL,
  [cRoleDescription] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_Role] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_RoleClaim
-- ----------------------------
CREATE TABLE [dbo].[ta_RoleClaim] (
  [cRoleId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cClaimName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_RoleClaim] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_SystemSession
-- ----------------------------
CREATE TABLE [dbo].[ta_SystemSession] (
  [cSystemSessionId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cSystemSessionAccountId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cSystemSessionHash] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cSystemSessionState] int  NOT NULL,
  [cSystemSessionExpiry] datetime  NOT NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_SystemSession] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_User
-- ----------------------------
CREATE TABLE [dbo].[ta_User] (
  [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserAccount] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cContactId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserState] int  NOT NULL,
  [cUserIsAdmin] bit  NOT NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_User] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_UserClaim
-- ----------------------------
CREATE TABLE [dbo].[ta_UserClaim] (
  [cUserClaimId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserClaimName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserClaimStart] datetime  NOT NULL,
  [cUserClaimExpiry] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_UserClaim] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_UserCredential
-- ----------------------------
CREATE TABLE [dbo].[ta_UserCredential] (
  [cCredentialId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cCredentialType] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cCredentialState] int  NOT NULL,
  [cCredentialKey] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cCredentialSecret] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_UserCredential] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_UserRole
-- ----------------------------
CREATE TABLE [dbo].[ta_UserRole] (
  [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cRoleId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserRoleStart] datetime  NULL,
  [cUserRoleExpiry] datetime  NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_UserRole] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_UserSession
-- ----------------------------
CREATE TABLE [dbo].[ta_UserSession] (
  [cUserSessionId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserSessionHash] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUserSessionState] int  NOT NULL,
  [cUserSessionExpiry] datetime  NOT NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_UserSession] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Primary Key structure for table ta_Address
-- ----------------------------
ALTER TABLE [dbo].[ta_Address] ADD CONSTRAINT [PK_ta_Address_1] PRIMARY KEY CLUSTERED ([cAddressId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_Comm
-- ----------------------------
ALTER TABLE [dbo].[ta_Comm] ADD CONSTRAINT [PK_ta_Comm_1] PRIMARY KEY CLUSTERED ([cCommId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_Contact
-- ----------------------------
ALTER TABLE [dbo].[ta_Contact] ADD CONSTRAINT [PK_ta_Contact] PRIMARY KEY CLUSTERED ([cContactId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Indexes structure for table ta_Log
-- ----------------------------
CREATE NONCLUSTERED INDEX [IX_ta_Log_Entity]
ON [dbo].[ta_Log] (
  [cLogEntity] ASC,
  [cLogEntityId] ASC,
  [datestamp] DESC
)
GO

CREATE NONCLUSTERED INDEX [IX_ta_Log_User]
ON [dbo].[ta_Log] (
  [cUserId] ASC,
  [datestamp] DESC
)
GO

CREATE NONCLUSTERED INDEX [IX_ta_Log_ModuleAction]
ON [dbo].[ta_Log] (
  [cLogModule] ASC,
  [cLogAction] ASC,
  [datestamp] DESC
)
GO

-- ----------------------------
-- Primary Key structure for table ta_Log
-- ----------------------------
ALTER TABLE [dbo].[ta_Log] ADD CONSTRAINT [PK_ta_Log] PRIMARY KEY CLUSTERED ([cLogId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_Meta
-- ----------------------------
ALTER TABLE [dbo].[ta_Meta] ADD CONSTRAINT [PK_ta_Meta] PRIMARY KEY CLUSTERED ([cMetaKey])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Uniques structure for table ta_Role
-- ----------------------------
ALTER TABLE [dbo].[ta_Role] ADD CONSTRAINT [UQ_ta_Role_cRoleName] UNIQUE NONCLUSTERED ([cRoleName] ASC)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_Role
-- ----------------------------
ALTER TABLE [dbo].[ta_Role] ADD CONSTRAINT [PK_ta_Role] PRIMARY KEY CLUSTERED ([cRoleId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_RoleClaim
-- ----------------------------
ALTER TABLE [dbo].[ta_RoleClaim] ADD CONSTRAINT [PK_ta_RoleClaim] PRIMARY KEY CLUSTERED ([cRoleId], [cClaimName])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Indexes structure for table ta_SystemSession
-- ----------------------------
CREATE UNIQUE NONCLUSTERED INDEX [UQ_ta_SystemSession_Hash]
ON [dbo].[ta_SystemSession] (
  [cSystemSessionHash] ASC
)
GO

CREATE NONCLUSTERED INDEX [IX_ta_SystemSession_cSystemSessionAccountId]
ON [dbo].[ta_SystemSession] (
  [cSystemSessionAccountId] ASC
)
GO

-- ----------------------------
-- Primary Key structure for table ta_SystemSession
-- ----------------------------
ALTER TABLE [dbo].[ta_SystemSession] ADD CONSTRAINT [PK_ta_SystemSession] PRIMARY KEY CLUSTERED ([cSystemSessionId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Uniques structure for table ta_User
-- ----------------------------
ALTER TABLE [dbo].[ta_User] ADD CONSTRAINT [UQ_ta_User_cUserAccount] UNIQUE NONCLUSTERED ([cUserAccount] ASC)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_User
-- ----------------------------
ALTER TABLE [dbo].[ta_User] ADD CONSTRAINT [PK_ta_User] PRIMARY KEY CLUSTERED ([cUserId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Uniques structure for table ta_UserClaim
-- ----------------------------
ALTER TABLE [dbo].[ta_UserClaim] ADD CONSTRAINT [UQ_ta_UserClaim_cUserId_cUserClaimName] UNIQUE NONCLUSTERED ([cUserId] ASC, [cUserClaimName] ASC)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_UserClaim
-- ----------------------------
ALTER TABLE [dbo].[ta_UserClaim] ADD CONSTRAINT [PK_ta_UserClaim_1] PRIMARY KEY CLUSTERED ([cUserClaimId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Uniques structure for table ta_UserCredential
-- ----------------------------
ALTER TABLE [dbo].[ta_UserCredential] ADD CONSTRAINT [UQ_ta_UserCredential_cUserId_cCredentialType] UNIQUE NONCLUSTERED ([cUserId] ASC, [cCredentialType] ASC)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_UserCredential
-- ----------------------------
ALTER TABLE [dbo].[ta_UserCredential] ADD CONSTRAINT [PK_ta_UserCredential] PRIMARY KEY CLUSTERED ([cCredentialId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_UserRole
-- ----------------------------
ALTER TABLE [dbo].[ta_UserRole] ADD CONSTRAINT [PK_ta_UserRole] PRIMARY KEY CLUSTERED ([cUserId], [cRoleId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Indexes structure for table ta_UserSession
-- ----------------------------
CREATE UNIQUE NONCLUSTERED INDEX [UQ_ta_UserSession_Hash]
ON [dbo].[ta_UserSession] (
  [cUserSessionHash] ASC
)
GO

CREATE NONCLUSTERED INDEX [IX_ta_UserSession_cUserId]
ON [dbo].[ta_UserSession] (
  [cUserId] ASC
)
GO

-- ----------------------------
-- Primary Key structure for table ta_UserSession
-- ----------------------------
ALTER TABLE [dbo].[ta_UserSession] ADD CONSTRAINT [PK_ta_UserSession] PRIMARY KEY CLUSTERED ([cUserSessionId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Foreign Keys structure for table ta_Address
-- ----------------------------
ALTER TABLE [dbo].[ta_Address] ADD CONSTRAINT [FK_ta_Address_ta_Contact] FOREIGN KEY ([cContactId]) REFERENCES [dbo].[ta_Contact] ([cContactId]) ON DELETE CASCADE ON UPDATE CASCADE
GO

-- ----------------------------
-- Foreign Keys structure for table ta_Comm
-- ----------------------------
ALTER TABLE [dbo].[ta_Comm] ADD CONSTRAINT [FK_ta_Comm_ta_Contact] FOREIGN KEY ([cContactId]) REFERENCES [dbo].[ta_Contact] ([cContactId]) ON DELETE CASCADE ON UPDATE CASCADE
GO

-- ----------------------------
-- Foreign Keys structure for table ta_Contact
-- ----------------------------
ALTER TABLE [dbo].[ta_Contact] ADD CONSTRAINT [FK_ta_Contact_ta_Address] FOREIGN KEY ([cContactDefaultAddress_cAddressId]) REFERENCES [dbo].[ta_Address] ([cAddressId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

ALTER TABLE [dbo].[ta_Contact] ADD CONSTRAINT [FK_ta_Contact_ta_Comm] FOREIGN KEY ([cContactDefaultComm_cCommId]) REFERENCES [dbo].[ta_Comm] ([cCommId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

-- ----------------------------
-- Foreign Keys structure for table ta_Log
-- ----------------------------
ALTER TABLE [dbo].[ta_Log] ADD CONSTRAINT [FK_ta_Log_ta_User] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

-- ----------------------------
-- Foreign Keys structure for table ta_RoleClaim
-- ----------------------------
ALTER TABLE [dbo].[ta_RoleClaim] ADD CONSTRAINT [FK_ta_RoleClaim_ta_Role] FOREIGN KEY ([cRoleId]) REFERENCES [dbo].[ta_Role] ([cRoleId]) ON DELETE CASCADE ON UPDATE CASCADE
GO

-- ----------------------------
-- Foreign Keys structure for table ta_User
-- ----------------------------
ALTER TABLE [dbo].[ta_User] ADD CONSTRAINT [FK_ta_User_ta_Contact] FOREIGN KEY ([cContactId]) REFERENCES [dbo].[ta_Contact] ([cContactId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

-- ----------------------------
-- Foreign Keys structure for table ta_UserClaim
-- ----------------------------
ALTER TABLE [dbo].[ta_UserClaim] ADD CONSTRAINT [FK_ta_UserClaim_ta_User] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE CASCADE ON UPDATE CASCADE
GO

-- ----------------------------
-- Foreign Keys structure for table ta_UserCredential
-- ----------------------------
ALTER TABLE [dbo].[ta_UserCredential] ADD CONSTRAINT [FK_ta_UserCredential_ta_User] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE CASCADE ON UPDATE CASCADE
GO

-- ----------------------------
-- Foreign Keys structure for table ta_UserRole
-- ----------------------------
ALTER TABLE [dbo].[ta_UserRole] ADD CONSTRAINT [FK_ta_UserRole_ta_Role] FOREIGN KEY ([cRoleId]) REFERENCES [dbo].[ta_Role] ([cRoleId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

ALTER TABLE [dbo].[ta_UserRole] ADD CONSTRAINT [FK_ta_UserRole_ta_User] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

-- ----------------------------
-- Foreign Keys structure for table ta_UserSession
-- ----------------------------
ALTER TABLE [dbo].[ta_UserSession] ADD CONSTRAINT [FK_ta_UserSession_ta_User] FOREIGN KEY ([cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

-- ============================================================================
-- 2. Robot
-- ============================================================================

-- Shared robot identities, independent of Container registry. Identity columns use cRobot*.
SET XACT_ABORT ON;
GO
--region ta_Robot
IF OBJECT_ID(N'[dbo].[ta_Robot]', N'U') IS NULL
   BEGIN
      CREATE TABLE [dbo].[ta_Robot]
      (
         [cRobotId]            char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cRobotName]          varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cRobotOwner_cUserId] char(26)     COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         [cRobotState]         int          NOT NULL,
         [cRobotDescription]   varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         -- SHA-256 hex dari token; tokennya sendiri tidak pernah disimpan.
         [cRobotTokenHash]     varchar(64)  COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
         [cRobotTokenPrefix]   varchar(20)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
         [cRobotTokenExpiry]   datetime     NULL,
         [cRobotTokenLastUsed] datetime     NULL,
         [ustamp]                 datetime     NOT NULL,
         [datestamp]              datetime     NOT NULL,
         [json_object]            nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
         CONSTRAINT [FK_ta_Robot_Owner] FOREIGN KEY ([cRobotOwner_cUserId]) REFERENCES [dbo].[ta_User] ([cUserId]) ON DELETE SET NULL,
         CONSTRAINT [PK_ta_CtnRobot] PRIMARY KEY CLUSTERED ([cRobotId]),
         CONSTRAINT [UQ_ta_CtnRobot_cRobotName] UNIQUE NONCLUSTERED ([cRobotName]),
         CONSTRAINT [UQ_ta_CtnRobot_cRobotTokenHash] UNIQUE NONCLUSTERED ([cRobotTokenHash])
      );
   END;
GO
--endregion
GO

-- ============================================================================
-- 3. View
-- ============================================================================

--region vi_Address
DROP VIEW IF EXISTS vi_Address;
GO
CREATE VIEW vi_Address AS
SELECT
   a.cAddressId
   , a.cContactId
   , a.cAddressName
   , a.cAddressState
   , a.cAddressLocation
   , a.cAddressZip
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Address AS a
GO

SELECT
   a.cAddressId
   , a.cContactId
   , a.cAddressName
   , a.cAddressState
   , a.cAddressLocation
   , a.cAddressZip
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM vi_Address AS a
GO
--endregion

--region vi_Comm
DROP VIEW IF EXISTS vi_Comm;
GO
CREATE VIEW vi_Comm AS
SELECT
   a.cCommId
   , a.cContactId

   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Comm AS a
GO

SELECT
   a.cCommId
   , a.cContactId
   , a.cCommType
   , a.cCommState
   , a.cCommValue
   , a.cCommNote
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM vi_Comm AS a
GO
--endregion

--region vi_Contact
DROP VIEW IF EXISTS vi_Contact;
GO
CREATE VIEW vi_Contact AS
SELECT
   a.cContactId
   , a.cContactFullName
   , a.cContactState
   , a.cContactType
   , a.cContactNote
   , a.cContactDefaultAddress_cAddressId
   , a.cContactDefaultComm_cCommId
   , a.ustamp
   , a.datestamp
   , a.json_object
   , b.cAddressName
   , b.cAddressLocation
   , b.cAddressZip
   , c.cCommType
   , c.cCommState
   , c.cCommValue
   , c.cCommNote
FROM ta_Contact AS a
LEFT OUTER JOIN ta_Address AS b
   ON a.cContactDefaultAddress_cAddressId = b.cAddressId
LEFT OUTER JOIN ta_Comm AS c
   ON a.cContactDefaultComm_cCommId = c.cCommId
GO
--endregion

--region vi_Role
DROP VIEW IF EXISTS vi_Role;
GO
CREATE VIEW vi_Role AS
SELECT
   a.cRoleId
   , a.cRoleName
   , a.cRoleState
   , a.cRoleDescription
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_Role AS a
GO
--endregion

--region vi_User
DROP VIEW IF EXISTS vi_User;
GO
CREATE VIEW vi_User AS
SELECT
   a.cUserId
   , a.cUserAccount
   , a.cContactId
   , a.cUserState
   , a.cUserIsAdmin
   , a.ustamp
   , a.datestamp
   , a.json_object
   , b.cContactFullName
   , b.cContactState
   , b.cContactType
   , b.cContactNote
   , c.cAddressName
   , c.cAddressLocation
   , c.cAddressZip
   , d.cCommType
   , d.cCommState
   , d.cCommValue
   , d.cCommNote
FROM ta_User AS a
INNER JOIN ta_Contact AS b
   ON a.cContactId = b.cContactId
INNER JOIN ta_Comm AS d
   ON b.cContactDefaultComm_cCommId = d.cCommId
LEFT OUTER JOIN ta_Address AS c
   ON b.cContactDefaultAddress_cAddressId = c.cAddressId
GO
--endregion

--region vi_UserCredential
DROP VIEW IF EXISTS vi_UserCredential;
GO
CREATE VIEW vi_UserCredential AS
SELECT
   a.cCredentialId
   , a.cUserId
   , a.cCredentialType
   , a.cCredentialState
   , a.cCredentialKey
   , a.cCredentialSecret
   , a.ustamp
   , a.datestamp
   , a.json_object
FROM ta_UserCredential AS a
GO
--endregion
