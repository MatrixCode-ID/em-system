/*
    080-tenant.sql - tenant (company) table of the business module manager.
    Requires: sets/000-ulid.sql (dbo.NewUlidString). Re-runnable: creates objects only when missing.
    Run views/vi_Tenant.sql after this file.

    A tenant may have an optional parent tenant (hierarchy). Tenants are never deleted physically:
    cTenantState 0 = active, -1 = inactive. No rows are seeded.
*/
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[ta_Tenant]', N'U') IS NULL
BEGIN
   CREATE TABLE [dbo].[ta_Tenant] (
      [cTenantId]                   char(26)      NOT NULL CONSTRAINT [DF_ta_Tenant_cTenantId] DEFAULT ([dbo].[NewUlidString]()),
      [cTenantParent_cTenantId]     char(26)      NULL,
      [cTenantName]                 varchar(255)  NOT NULL,
      [cTenantCode]                 varchar(100)  NOT NULL,   -- stable short key chosen by the admin
      [cTenantState]                int           NOT NULL CONSTRAINT [DF_ta_Tenant_cTenantState] DEFAULT (0),    -- 0 active, -1 inactive
      [cTenantOrder]                int           NOT NULL CONSTRAINT [DF_ta_Tenant_cTenantOrder] DEFAULT (-1),   -- display order among siblings
      [cTenantNote]                 varchar(500)  NULL,
      [ustamp]                      datetime      NOT NULL CONSTRAINT [DF_ta_Tenant_ustamp] DEFAULT (getdate()),
      [datestamp]                   datetime      NOT NULL CONSTRAINT [DF_ta_Tenant_datestamp] DEFAULT (getdate()),
      [json_object]                 nvarchar(max) NULL,
      CONSTRAINT [PK_ta_Tenant] PRIMARY KEY CLUSTERED ([cTenantId]),
      CONSTRAINT [UQ_ta_Tenant_cTenantCode] UNIQUE ([cTenantCode])
   );
END
GO

-- No ON DELETE action: tenants are deactivated, never deleted.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_Tenant_ta_Tenant_Parent')
   ALTER TABLE [dbo].[ta_Tenant] ADD CONSTRAINT [FK_ta_Tenant_ta_Tenant_Parent]
      FOREIGN KEY ([cTenantParent_cTenantId]) REFERENCES [dbo].[ta_Tenant] ([cTenantId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_Tenant_cTenantParent_cTenantId'
               AND object_id = OBJECT_ID(N'[dbo].[ta_Tenant]'))
   CREATE INDEX [IX_ta_Tenant_cTenantParent_cTenantId] ON [dbo].[ta_Tenant] ([cTenantParent_cTenantId]);
GO
