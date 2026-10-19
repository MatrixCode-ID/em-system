/* Tenant organization. Requires sets/000-ulid.sql, tables/010-core.sql and tables/080-tenant.sql.
   Re-runnable; creates missing objects only. No seed data or physical deletion. */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'[dbo].[ta_TenantDept]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantDept] (
   [cTenantDeptId]                     char(26)      NOT NULL CONSTRAINT [DF_ta_TenantDept_cTenantDeptId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantDeptParent_cTenantDeptId]   char(26)      NULL,
   [cTenantDeptCode]                   varchar(50)   NOT NULL,   -- unique per tenant
   [cTenantDeptName]                   varchar(255)  NOT NULL,
   [cTenantDeptAbv]                    varchar(10)   NULL,
   [cTenantDeptState]                  int           NOT NULL CONSTRAINT [DF_ta_TenantDept_cTenantDeptState] DEFAULT (0),   -- 0 active, -1 inactive
   [cTenantDeptOrder]                  int           NOT NULL CONSTRAINT [DF_ta_TenantDept_cTenantDeptOrder] DEFAULT (-1),
   [cTenantDeptNote]                   varchar(500)  NULL,
   [cTenantDeptHead_cTenantEmpId]      char(26)      NULL,       -- an active employee of this department
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantDept_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantDept_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantDept] PRIMARY KEY CLUSTERED ([cTenantDeptId]),
   CONSTRAINT [UQ_ta_TenantDept_cTenantId_cTenantDeptCode] UNIQUE ([cTenantId], [cTenantDeptCode]),
   CONSTRAINT [UQ_ta_TenantDept_cTenantDeptId_cTenantId] UNIQUE ([cTenantDeptId], [cTenantId])
);
END
GO

IF OBJECT_ID(N'[dbo].[ta_TenantEmp]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantEmp] (
   [cTenantEmpId]                      char(26)      NOT NULL CONSTRAINT [DF_ta_TenantEmp_cTenantEmpId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cContactId]                        char(26)      NOT NULL,
   [cTenantDeptId]                     char(26)      NULL,
   [cTenantEmpCode]                    varchar(50)   NOT NULL,   -- employee number, free text, unique per tenant
   [cTenantEmpPosition]                varchar(255)  NULL,       -- position title, free text
   [cTenantEmpState]                   int           NOT NULL CONSTRAINT [DF_ta_TenantEmp_cTenantEmpState] DEFAULT (0),     -- 0 active, -1 inactive
   [cTenantEmpNote]                    varchar(500)  NULL,
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantEmp_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantEmp_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantEmp] PRIMARY KEY CLUSTERED ([cTenantEmpId]),
   CONSTRAINT [UQ_ta_TenantEmp_cTenantId_cTenantEmpCode] UNIQUE ([cTenantId], [cTenantEmpCode]),
   CONSTRAINT [UQ_ta_TenantEmp_cTenantId_cContactId] UNIQUE ([cTenantId], [cContactId]),
   CONSTRAINT [UQ_ta_TenantEmp_cTenantEmpId_cTenantDeptId] UNIQUE ([cTenantEmpId], [cTenantDeptId])
);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantDept_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantDept]'))
   ALTER TABLE [dbo].[ta_TenantDept] ADD CONSTRAINT [FK_ta_TenantDept_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantDept_ta_TenantDept_Parent' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantDept]'))
   ALTER TABLE [dbo].[ta_TenantDept] ADD CONSTRAINT [FK_ta_TenantDept_ta_TenantDept_Parent]
      FOREIGN KEY (cTenantDeptParent_cTenantDeptId, cTenantId) REFERENCES [dbo].[ta_TenantDept] (cTenantDeptId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantDept_ta_TenantEmp_Head' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantDept]'))
   ALTER TABLE [dbo].[ta_TenantDept] ADD CONSTRAINT [FK_ta_TenantDept_ta_TenantEmp_Head]
      FOREIGN KEY (cTenantDeptHead_cTenantEmpId, cTenantDeptId) REFERENCES [dbo].[ta_TenantEmp] (cTenantEmpId, cTenantDeptId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantEmp_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantEmp]'))
   ALTER TABLE [dbo].[ta_TenantEmp] ADD CONSTRAINT [FK_ta_TenantEmp_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantEmp_ta_Contact' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantEmp]'))
   ALTER TABLE [dbo].[ta_TenantEmp] ADD CONSTRAINT [FK_ta_TenantEmp_ta_Contact]
      FOREIGN KEY (cContactId) REFERENCES [dbo].[ta_Contact] (cContactId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantEmp_ta_TenantDept' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantEmp]'))
   ALTER TABLE [dbo].[ta_TenantEmp] ADD CONSTRAINT [FK_ta_TenantEmp_ta_TenantDept]
      FOREIGN KEY (cTenantDeptId, cTenantId) REFERENCES [dbo].[ta_TenantDept] (cTenantDeptId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantDept_cTenantDeptParent_cTenantDeptId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantDept]'))
   CREATE INDEX [IX_ta_TenantDept_cTenantDeptParent_cTenantDeptId] ON [dbo].[ta_TenantDept] ([cTenantDeptParent_cTenantDeptId]) WHERE [cTenantDeptParent_cTenantDeptId] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantEmp_cContactId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantEmp]'))
   CREATE INDEX [IX_ta_TenantEmp_cContactId] ON [dbo].[ta_TenantEmp] ([cContactId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantEmp_cTenantDeptId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantEmp]'))
   CREATE INDEX [IX_ta_TenantEmp_cTenantDeptId] ON [dbo].[ta_TenantEmp] ([cTenantDeptId]) WHERE [cTenantDeptId] IS NOT NULL;
GO

