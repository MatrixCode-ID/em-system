/* Products of a tenant. Requires sets/000-ulid.sql, tables/080-tenant.sql and tables/100-business.sql (ta_Unit).
   An installation from before 2026-11-12 runs updates/20261112-Unit-Stamp.sql as well.
   Re-runnable; creates missing objects only. No seed data or physical deletion. */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'[dbo].[ta_Unit]', N'U') IS NULL OR OBJECT_ID(N'[dbo].[ta_Tenant]', N'U') IS NULL
   THROW 51000, 'Install tables/080-tenant.sql and tables/100-business.sql before this script.', 1;
GO
IF OBJECT_ID(N'[dbo].[ta_TenantProductType]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantProductType] (
   [cTenantProductTypeId]              char(26)      NOT NULL CONSTRAINT [DF_ta_TenantProductType_cTenantProductTypeId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantProductTypeCode]            varchar(50)   NOT NULL,   -- unique per tenant
   [cTenantProductTypeName]            varchar(255)  NOT NULL,
   [cTenantProductTypeKind]            int           NOT NULL,   -- 1 goods, 2 manufacture base, 3 product component, 4 non-stock, 5 service, 6 financial, 7 generic transaction
   [cTenantProductTypeState]           int           NOT NULL CONSTRAINT [DF_ta_TenantProductType_cTenantProductTypeState] DEFAULT (0),   -- 0 active, -1 inactive
   [cTenantProductTypeOrder]           int           NOT NULL CONSTRAINT [DF_ta_TenantProductType_cTenantProductTypeOrder] DEFAULT (-1),
   [cTenantProductTypeNote]            varchar(500)  NULL,
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantProductType_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantProductType_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantProductType] PRIMARY KEY CLUSTERED ([cTenantProductTypeId]),
   CONSTRAINT [UQ_ta_TenantProductType_cTenantId_cTenantProductTypeCode] UNIQUE ([cTenantId], [cTenantProductTypeCode]),
   CONSTRAINT [UQ_ta_TenantProductType_cTenantProductTypeId_cTenantId] UNIQUE ([cTenantProductTypeId], [cTenantId])
);
END
GO

IF OBJECT_ID(N'[dbo].[ta_TenantProduct]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantProduct] (
   [cTenantProductId]                  char(26)      NOT NULL CONSTRAINT [DF_ta_TenantProduct_cTenantProductId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantProductTypeId]              char(26)      NOT NULL,   -- a product type of this tenant
   [cTenantProductParentId]            char(26)      NULL,       -- a product of this tenant; NULL for a root product
   [cUnitId]                           varchar(50)   NOT NULL,
   [cTenantProductCode]                varchar(50)   NOT NULL,   -- entered by hand, unique per tenant
   [cTenantProductName]                varchar(255)  NOT NULL,
   [cTenantProductState]               int           NOT NULL CONSTRAINT [DF_ta_TenantProduct_cTenantProductState] DEFAULT (0),   -- 0 active, -1 inactive
   [cTenantProductOrder]               int           NOT NULL CONSTRAINT [DF_ta_TenantProduct_cTenantProductOrder] DEFAULT (-1),
   [cTenantProductNote]                varchar(500)  NULL,
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantProduct_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantProduct_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantProduct] PRIMARY KEY CLUSTERED ([cTenantProductId]),
   CONSTRAINT [UQ_ta_TenantProduct_cTenantId_cTenantProductCode] UNIQUE ([cTenantId], [cTenantProductCode]),
   CONSTRAINT [UQ_ta_TenantProduct_cTenantProductId_cTenantId] UNIQUE ([cTenantProductId], [cTenantId])
);
END
GO

-- A foreign key needs the same collation on both sides, and the unit code has its own, whatever the database default is.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantProduct_ta_Unit' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
BEGIN
   DECLARE @unitCollation sysname = (SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ta_Unit]') AND name = N'cUnitId');
   DECLARE @productCollation sysname = (SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]') AND name = N'cUnitId');
   IF @unitCollation IS NOT NULL AND @unitCollation <> @productCollation
      EXEC(N'ALTER TABLE [dbo].[ta_TenantProduct] ALTER COLUMN [cUnitId] varchar(50) COLLATE ' + @unitCollation + N' NOT NULL;');
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantProductType_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantProductType]'))
   ALTER TABLE [dbo].[ta_TenantProductType] ADD CONSTRAINT [FK_ta_TenantProductType_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantProduct_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
   ALTER TABLE [dbo].[ta_TenantProduct] ADD CONSTRAINT [FK_ta_TenantProduct_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantProduct_ta_TenantProductType' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
   ALTER TABLE [dbo].[ta_TenantProduct] ADD CONSTRAINT [FK_ta_TenantProduct_ta_TenantProductType]
      FOREIGN KEY (cTenantProductTypeId, cTenantId) REFERENCES [dbo].[ta_TenantProductType] (cTenantProductTypeId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantProduct_ta_TenantProduct_Parent' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
   ALTER TABLE [dbo].[ta_TenantProduct] ADD CONSTRAINT [FK_ta_TenantProduct_ta_TenantProduct_Parent]
      FOREIGN KEY (cTenantProductParentId, cTenantId) REFERENCES [dbo].[ta_TenantProduct] (cTenantProductId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantProduct_ta_Unit' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
   ALTER TABLE [dbo].[ta_TenantProduct] ADD CONSTRAINT [FK_ta_TenantProduct_ta_Unit]
      FOREIGN KEY (cUnitId) REFERENCES [dbo].[ta_Unit] (cUnitId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantProduct_Tenant_Parent_Order' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
   CREATE INDEX [IX_ta_TenantProduct_Tenant_Parent_Order] ON [dbo].[ta_TenantProduct] ([cTenantId], [cTenantProductParentId], [cTenantProductOrder]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantProduct_cTenantId_cTenantProductTypeId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
   CREATE INDEX [IX_ta_TenantProduct_cTenantId_cTenantProductTypeId] ON [dbo].[ta_TenantProduct] ([cTenantId], [cTenantProductTypeId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantProduct_cUnitId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantProduct]'))
   CREATE INDEX [IX_ta_TenantProduct_cUnitId] ON [dbo].[ta_TenantProduct] ([cUnitId]);
GO
