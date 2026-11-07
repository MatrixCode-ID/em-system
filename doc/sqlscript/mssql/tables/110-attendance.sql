/* Attendance setup. Requires sets/000-ulid.sql, tables/010-core.sql, tables/080-tenant.sql and tables/090-tenant-org.sql.
   Re-runnable; creates missing objects only. No seed data or physical deletion.
   Work date means the date a shift starts; the end time of a shift is calculated, never stored. */
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'[dbo].[ta_TenantSchedTemplate]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantSchedTemplate] (
   [cTenantSchedTemplateId]            char(26)      NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplate_cTenantSchedTemplateId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantSchedTemplateCode]          varchar(10)   NOT NULL,   -- unique per tenant
   [cTenantSchedTemplateName]          varchar(255)  NOT NULL,
   [cTenantSchedTemplateStart]         time(0)       NOT NULL,
   [cTenantSchedTemplateBreak]         int           NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplate_cTenantSchedTemplateBreak] DEFAULT (0),   -- minutes
   [cTenantSchedTemplateState]         int           NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplate_cTenantSchedTemplateState] DEFAULT (0),   -- 0 active, -1 inactive
   [cTenantSchedTemplateOrder]         int           NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplate_cTenantSchedTemplateOrder] DEFAULT (-1),
   [cTenantSchedTemplateNote]          varchar(500)  NULL,
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplate_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplate_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantSchedTemplate] PRIMARY KEY CLUSTERED ([cTenantSchedTemplateId]),
   CONSTRAINT [UQ_ta_TenantSchedTemplate_cTenantId_cTenantSchedTemplateCode] UNIQUE ([cTenantId], [cTenantSchedTemplateCode]),
   CONSTRAINT [UQ_ta_TenantSchedTemplate_cTenantSchedTemplateId_cTenantId] UNIQUE ([cTenantSchedTemplateId], [cTenantId])
);
END
GO

IF OBJECT_ID(N'[dbo].[ta_TenantSchedTemplateDay]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantSchedTemplateDay] (
   [cTenantSchedTemplateDayId]         char(26)      NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplateDay_cTenantSchedTemplateDayId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantSchedTemplateId]            char(26)      NOT NULL,
   [cTenantSchedTemplateDayWeekday]    int           NOT NULL,   -- 1 Monday to 7 Sunday, the weekday a shift starts on
   [cTenantSchedTemplateDayHours]      decimal(4,2)  NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplateDay_cTenantSchedTemplateDayHours] DEFAULT (0),   -- 0 is a day off
   [cTenantSchedTemplateDayBreak]      int           NULL,       -- minutes; empty uses the template's break
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplateDay_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedTemplateDay_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantSchedTemplateDay] PRIMARY KEY CLUSTERED ([cTenantSchedTemplateDayId]),
   CONSTRAINT [UQ_ta_TenantSchedTemplateDay_Template_Weekday] UNIQUE ([cTenantSchedTemplateId], [cTenantSchedTemplateDayWeekday])
);
END
GO

IF OBJECT_ID(N'[dbo].[ta_TenantSchedSetting]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantSchedSetting] (
   [cTenantSchedSettingId]             char(26)      NOT NULL CONSTRAINT [DF_ta_TenantSchedSetting_cTenantSchedSettingId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantDeptId]                     char(26)      NULL,       -- empty on the tenant row
   [cTenantSchedSettingKey]            char(26)      NOT NULL,   -- department id, or tenant id on the tenant row: NULL is not unique the same way in every database
   [cTenantSchedSettingEnable]         int           NOT NULL CONSTRAINT [DF_ta_TenantSchedSetting_cTenantSchedSettingEnable] DEFAULT (0),   -- 0 inherit, 1 enabled, -1 disabled
   [cTenantSchedSettingMode]           int           NOT NULL CONSTRAINT [DF_ta_TenantSchedSetting_cTenantSchedSettingMode] DEFAULT (0),     -- 0 inherit (built-in on the tenant row), 1 template, 2 rotation
   [cTenantSchedTemplateId]            char(26)      NULL,
   [cTenantSchedSettingStepWeeks]      int           NULL,
   [cTenantSchedSettingCycleStart]     date          NULL,       -- Day 1 of the rotation
   [cTenantSchedSettingPeriodDays]     int           NULL,       -- tenant row only; empty means 14
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedSetting_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedSetting_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantSchedSetting] PRIMARY KEY CLUSTERED ([cTenantSchedSettingId]),
   CONSTRAINT [UQ_ta_TenantSchedSetting_cTenantId_cTenantSchedSettingKey] UNIQUE ([cTenantId], [cTenantSchedSettingKey]),
   CONSTRAINT [UQ_ta_TenantSchedSetting_cTenantSchedSettingId_cTenantId] UNIQUE ([cTenantSchedSettingId], [cTenantId])
);
END
GO

IF OBJECT_ID(N'[dbo].[ta_TenantSchedSettingStep]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantSchedSettingStep] (
   [cTenantSchedSettingStepId]         char(26)      NOT NULL CONSTRAINT [DF_ta_TenantSchedSettingStep_cTenantSchedSettingStepId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantSchedSettingId]             char(26)      NOT NULL,
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantSchedSettingStepOrder]      int           NOT NULL,
   [cTenantSchedTemplateId]            char(26)      NOT NULL,
   [cTenantSchedSettingStepState]      int           NOT NULL CONSTRAINT [DF_ta_TenantSchedSettingStep_cTenantSchedSettingStepState] DEFAULT (0),   -- 0 active, -1 inactive; reused by order
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedSettingStep_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantSchedSettingStep_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantSchedSettingStep] PRIMARY KEY CLUSTERED ([cTenantSchedSettingStepId]),
   CONSTRAINT [UQ_ta_TenantSchedSettingStep_Setting_Order] UNIQUE ([cTenantSchedSettingId], [cTenantSchedSettingStepOrder])
);
END
GO

IF OBJECT_ID(N'[dbo].[ta_TenantAttCode]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_TenantAttCode] (
   [cTenantAttCodeId]                  char(26)      NOT NULL CONSTRAINT [DF_ta_TenantAttCode_cTenantAttCodeId] DEFAULT ([dbo].[NewUlidString]()),
   [cTenantId]                         char(26)      NOT NULL,
   [cTenantAttCodeCode]                varchar(50)   NOT NULL,   -- unique per tenant
   [cTenantAttCodeName]                varchar(255)  NOT NULL,
   [cTenantAttCodeCategory]            int           NOT NULL,   -- 1 present, 2 paid absence, 3 unpaid absence, 4 day off
   [cTenantAttCodeState]               int           NOT NULL CONSTRAINT [DF_ta_TenantAttCode_cTenantAttCodeState] DEFAULT (0),   -- 0 active, -1 inactive
   [cTenantAttCodeOrder]               int           NOT NULL CONSTRAINT [DF_ta_TenantAttCode_cTenantAttCodeOrder] DEFAULT (-1),
   [cTenantAttCodeNote]                varchar(500)  NULL,
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_TenantAttCode_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_TenantAttCode_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_TenantAttCode] PRIMARY KEY CLUSTERED ([cTenantAttCodeId]),
   CONSTRAINT [UQ_ta_TenantAttCode_cTenantId_cTenantAttCodeCode] UNIQUE ([cTenantId], [cTenantAttCodeCode])
);
END
GO

IF OBJECT_ID(N'[dbo].[ta_PublicHoliday]', N'U') IS NULL
BEGIN
CREATE TABLE [dbo].[ta_PublicHoliday] (
   [cPublicHolidayId]                  char(26)      NOT NULL CONSTRAINT [DF_ta_PublicHoliday_cPublicHolidayId] DEFAULT ([dbo].[NewUlidString]()),
   [cPublicHolidayName]                varchar(255)  NOT NULL,
   [cPublicHolidayKind]                int           NOT NULL,   -- 1 fixed (month and day), 2 dated (one date)
   [cPublicHolidayMonth]               int           NULL,
   [cPublicHolidayDay]                 int           NULL,
   [cPublicHolidayDate]                date          NULL,
   [cPublicHolidayCollective]          bit           NOT NULL CONSTRAINT [DF_ta_PublicHoliday_cPublicHolidayCollective] DEFAULT (0),   -- a marker only, with no behaviour
   [cPublicHolidayState]               int           NOT NULL CONSTRAINT [DF_ta_PublicHoliday_cPublicHolidayState] DEFAULT (0),        -- 0 active, -1 removed
   [cPublicHolidayNote]                varchar(500)  NULL,
   [ustamp]                            datetime      NOT NULL CONSTRAINT [DF_ta_PublicHoliday_ustamp] DEFAULT (getdate()),
   [datestamp]                         datetime      NOT NULL CONSTRAINT [DF_ta_PublicHoliday_datestamp] DEFAULT (getdate()),
   [json_object]                       nvarchar(max) NULL,
   CONSTRAINT [PK_ta_PublicHoliday] PRIMARY KEY CLUSTERED ([cPublicHolidayId])
);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantSchedTemplate_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedTemplate]'))
   ALTER TABLE [dbo].[ta_TenantSchedTemplate] ADD CONSTRAINT [FK_ta_TenantSchedTemplate_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantSchedTemplateDay_ta_TenantSchedTemplate' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedTemplateDay]'))
   ALTER TABLE [dbo].[ta_TenantSchedTemplateDay] ADD CONSTRAINT [FK_ta_TenantSchedTemplateDay_ta_TenantSchedTemplate]
      FOREIGN KEY (cTenantSchedTemplateId) REFERENCES [dbo].[ta_TenantSchedTemplate] (cTenantSchedTemplateId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantSchedSetting_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSetting]'))
   ALTER TABLE [dbo].[ta_TenantSchedSetting] ADD CONSTRAINT [FK_ta_TenantSchedSetting_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantSchedSetting_ta_TenantDept' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSetting]'))
   ALTER TABLE [dbo].[ta_TenantSchedSetting] ADD CONSTRAINT [FK_ta_TenantSchedSetting_ta_TenantDept]
      FOREIGN KEY (cTenantDeptId, cTenantId) REFERENCES [dbo].[ta_TenantDept] (cTenantDeptId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantSchedSetting_ta_TenantSchedTemplate' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSetting]'))
   ALTER TABLE [dbo].[ta_TenantSchedSetting] ADD CONSTRAINT [FK_ta_TenantSchedSetting_ta_TenantSchedTemplate]
      FOREIGN KEY (cTenantSchedTemplateId, cTenantId) REFERENCES [dbo].[ta_TenantSchedTemplate] (cTenantSchedTemplateId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantSchedSettingStep_ta_TenantSchedSetting' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSettingStep]'))
   ALTER TABLE [dbo].[ta_TenantSchedSettingStep] ADD CONSTRAINT [FK_ta_TenantSchedSettingStep_ta_TenantSchedSetting]
      FOREIGN KEY (cTenantSchedSettingId, cTenantId) REFERENCES [dbo].[ta_TenantSchedSetting] (cTenantSchedSettingId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantSchedSettingStep_ta_TenantSchedTemplate' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSettingStep]'))
   ALTER TABLE [dbo].[ta_TenantSchedSettingStep] ADD CONSTRAINT [FK_ta_TenantSchedSettingStep_ta_TenantSchedTemplate]
      FOREIGN KEY (cTenantSchedTemplateId, cTenantId) REFERENCES [dbo].[ta_TenantSchedTemplate] (cTenantSchedTemplateId, cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ta_TenantAttCode_ta_Tenant' AND parent_object_id = OBJECT_ID(N'[dbo].[ta_TenantAttCode]'))
   ALTER TABLE [dbo].[ta_TenantAttCode] ADD CONSTRAINT [FK_ta_TenantAttCode_ta_Tenant]
      FOREIGN KEY (cTenantId) REFERENCES [dbo].[ta_Tenant] (cTenantId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantSchedSetting_cTenantDeptId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSetting]'))
   CREATE INDEX [IX_ta_TenantSchedSetting_cTenantDeptId] ON [dbo].[ta_TenantSchedSetting] ([cTenantDeptId]) WHERE [cTenantDeptId] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantSchedSetting_cTenantSchedTemplateId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSetting]'))
   CREATE INDEX [IX_ta_TenantSchedSetting_cTenantSchedTemplateId] ON [dbo].[ta_TenantSchedSetting] ([cTenantSchedTemplateId]) WHERE [cTenantSchedTemplateId] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_TenantSchedSettingStep_cTenantSchedTemplateId' AND object_id = OBJECT_ID(N'[dbo].[ta_TenantSchedSettingStep]'))
   CREATE INDEX [IX_ta_TenantSchedSettingStep_cTenantSchedTemplateId] ON [dbo].[ta_TenantSchedSettingStep] ([cTenantSchedTemplateId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_PublicHoliday_cPublicHolidayDate' AND object_id = OBJECT_ID(N'[dbo].[ta_PublicHoliday]'))
   CREATE INDEX [IX_ta_PublicHoliday_cPublicHolidayDate] ON [dbo].[ta_PublicHoliday] ([cPublicHolidayDate]) WHERE [cPublicHolidayDate] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ta_PublicHoliday_cPublicHolidayMonth_cPublicHolidayDay' AND object_id = OBJECT_ID(N'[dbo].[ta_PublicHoliday]'))
   CREATE INDEX [IX_ta_PublicHoliday_cPublicHolidayMonth_cPublicHolidayDay] ON [dbo].[ta_PublicHoliday] ([cPublicHolidayMonth], [cPublicHolidayDay]) WHERE [cPublicHolidayMonth] IS NOT NULL;
GO
