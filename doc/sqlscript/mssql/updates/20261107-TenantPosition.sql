-- Adds tenant positions and contact attachments to an installation that already has tables/090-tenant-org.sql, and
-- replaces the employee position text with a reference to a position. Text values that exist become positions of
-- their tenant before the column is removed, with the first 50 characters as the code. Safe to run again.
-- Run views/vi_TenantPosition.sql and views/vi_TenantEmp.sql afterwards, before starting an API that has these columns.
SET XACT_ABORT ON;
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.ta_TenantEmp', N'U') IS NULL
   THROW 51000, 'Install tables/090-tenant-org.sql before this update.', 1;
GO
IF OBJECT_ID(N'[dbo].[ta_TenantPosition]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantPosition] (
   [cTenantPositionId]                 char(26)      NOT NULL CONSTRAINT [DF_ta_TenantPosition_cTenantPositionId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantPositionCode]               varchar(50)   NOT NULL,   -- unique per tenant
   [cTenantPositionName]               varchar(255)  NOT NULL,
   [cTenantPositionState]              int           NOT NULL CONSTRAINT [DF_ta_TenantPosition_cTenantPositionState] DEFAULT (0),   -- 0 active, -1 inactive
   [cTenantPositionNote]               varchar(500)  NULL,
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantPosition_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantPosition_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantPosition] PRIMARY KEY CLUSTERED ([cTenantPositionId]),
   CONSTRAINT [UQ_ta_TenantPosition_cTenantId_cTenantPositionCode] UNIQUE ([cTenantId], [cTenantPositionCode]),
   CONSTRAINT [UQ_ta_TenantPosition_cTenantPositionId_cTenantId] UNIQUE ([cTenantPositionId], [cTenantId])
);
END
GO

-- An older installation keeps its table; updates/20261107-TenantPosition.sql then moves and removes the text column.
IF COL_LENGTH(N'dbo.ta_TenantEmp', N'cTenantPositionId') IS NULL
   ALTER TABLE [dbo].[ta_TenantEmp] ADD [cTenantPositionId] char(26) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantPosition_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantPosition]'))
   ALTER TABLE [dbo].[ta_TenantPosition] ADD CONSTRAINT [FK_ta_TenantPosition_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

-- Dynamic because this batch must still compile after the text column is gone.
IF COL_LENGTH(N'dbo.ta_TenantEmp', N'cTenantEmpPosition') IS NOT NULL
BEGIN
   BEGIN TRANSACTION;
   EXEC(N'
      INSERT INTO dbo.ta_TenantPosition (cTenantId, cTenantPositionCode, cTenantPositionName)
      SELECT e.cTenantId, LEFT(LTRIM(RTRIM(e.cTenantEmpPosition)), 50), MIN(LTRIM(RTRIM(e.cTenantEmpPosition)))
      FROM dbo.ta_TenantEmp e
      WHERE LTRIM(RTRIM(ISNULL(e.cTenantEmpPosition, ''''))) <> ''''
        AND NOT EXISTS (SELECT 1 FROM dbo.ta_TenantPosition p WHERE p.cTenantId = e.cTenantId AND p.cTenantPositionCode = LEFT(LTRIM(RTRIM(e.cTenantEmpPosition)), 50))
      GROUP BY e.cTenantId, LEFT(LTRIM(RTRIM(e.cTenantEmpPosition)), 50);
      UPDATE e SET e.cTenantPositionId = p.cTenantPositionId
      FROM dbo.ta_TenantEmp e
      JOIN dbo.ta_TenantPosition p ON p.cTenantId = e.cTenantId AND p.cTenantPositionCode = LEFT(LTRIM(RTRIM(e.cTenantEmpPosition)), 50)
      WHERE e.cTenantPositionId IS NULL AND LTRIM(RTRIM(ISNULL(e.cTenantEmpPosition, ''''))) <> '''';
      ALTER TABLE dbo.ta_TenantEmp DROP COLUMN cTenantEmpPosition;');
   COMMIT TRANSACTION;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantEmp_ta_TenantPosition' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantEmp]'))
   ALTER TABLE [dbo].[ta_TenantEmp] ADD CONSTRAINT [FK_ta_TenantEmp_ta_TenantPosition]
      FOREIGN KEY (cTenantPositionId, cTenantId) REFERENCES [dbo].[ta_TenantPosition] (cTenantPositionId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantEmp_cTenantPositionId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantEmp]'))
   CREATE INDEX [IX_ta_TenantEmp_cTenantPositionId] ON [dbo].[ta_TenantEmp] ([cTenantPositionId]) WHERE [cTenantPositionId] IS NOT NULL;
GO

IF OBJECT_ID(N'[dbo].[ta_ContactAttachment]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_ContactAttachment] (
   [cContactAttachmentId]              char(26)       NOT NULL CONSTRAINT [DF_ta_ContactAttachment_cContactAttachmentId] DEFAULT ([dbo].[NewUlidString]()),
   [cContactId]                        char(26)       NOT NULL,
   [cStoreObjectId]                    char(26)       NOT NULL,   -- object storage object; no foreign key, object storage is optional for a host
   [cContactAttachmentName]            nvarchar(255)  NOT NULL,
   [cContactAttachmentKind]            int            NOT NULL CONSTRAINT [DF_ta_ContactAttachment_cContactAttachmentKind] DEFAULT (99),   -- 1 photo, 2 ID card, 3 diploma, 99 other
   [cContactAttachmentNote]            nvarchar(500)  NULL,
   [cContactAttachmentState]           int            NOT NULL CONSTRAINT [DF_ta_ContactAttachment_cContactAttachmentState] DEFAULT (0),   -- 0 active, -1 inactive
   [cContactAttachmentOrder]           int            NOT NULL CONSTRAINT [DF_ta_ContactAttachment_cContactAttachmentOrder] DEFAULT (0),
   [ustamp]                            datetime       NOT NULL CONSTRAINT [DF_ta_ContactAttachment_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime       NOT NULL CONSTRAINT [DF_ta_ContactAttachment_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max)  NULL,
   CONSTRAINT [PK_ta_ContactAttachment] PRIMARY KEY CLUSTERED ([cContactAttachmentId])
);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_ContactAttachment_ta_Contact' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_ContactAttachment]'))
   ALTER TABLE [dbo].[ta_ContactAttachment] ADD CONSTRAINT [FK_ta_ContactAttachment_ta_Contact]
      FOREIGN KEY (cContactId) REFERENCES [dbo].[ta_Contact] (cContactId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_ContactAttachment_cContactId' AND object_id = OBJECT_ID(N'[dbo].[ta_ContactAttachment]'))
   CREATE INDEX [IX_ta_ContactAttachment_cContactId] ON [dbo].[ta_ContactAttachment] ([cContactId]);
GO
