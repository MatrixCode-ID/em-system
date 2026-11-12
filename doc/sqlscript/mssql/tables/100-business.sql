/*
    100 - Business objects: business partners, currencies, units, and employees.

    Run on the target database (USE [DatabaseName]) after tables/010-core.sql (ta_Contact).
    Meant for a new database (CREATE TABLE without checks).

    Contents: ta_BusinessPartner, ta_Currency, ta_Unit, ta_Emp with their primary keys and foreign keys.
    Products and product types live in tables/120-product.sql. An installation from before 2026-11-12 runs
    updates/20261112-Unit-Stamp.sql and updates/20261112-DropLegacyProduct.sql instead of this script.
*/

-- ----------------------------
-- Table structure for ta_BusinessPartner
-- ----------------------------
CREATE TABLE [dbo].[ta_BusinessPartner] (
  [cBusinessPartnerId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cContactId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_BusinessPartner] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Currency
-- ----------------------------
CREATE TABLE [dbo].[ta_Currency] (
  [cCurrencyId] varchar(25) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cCurrencyName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_Currency] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Emp
-- ----------------------------
CREATE TABLE [dbo].[ta_Emp] (
  [cEmpId] varchar(8) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cContactId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cEmpPositionCurent] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_Emp] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Unit
-- ----------------------------
CREATE TABLE [dbo].[ta_Unit] (
  [cUnitId] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUnitSymbol] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUnitDesc] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [ustamp] datetime  NOT NULL CONSTRAINT [DF_ta_Unit_ustamp] DEFAULT (getdate()),
  [datestamp] datetime  NOT NULL CONSTRAINT [DF_ta_Unit_datestamp] DEFAULT (getdate())
)
GO

ALTER TABLE [dbo].[ta_Unit] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Primary Key structure for table ta_BusinessPartner
-- ----------------------------
ALTER TABLE [dbo].[ta_BusinessPartner] ADD CONSTRAINT [PK_ta_BusinessPartner] PRIMARY KEY CLUSTERED ([cBusinessPartnerId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_Currency
-- ----------------------------
ALTER TABLE [dbo].[ta_Currency] ADD CONSTRAINT [PK__ta_Curre__90C325330E812541] PRIMARY KEY CLUSTERED ([cCurrencyId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_Emp
-- ----------------------------
ALTER TABLE [dbo].[ta_Emp] ADD CONSTRAINT [PK__ta_Emp__E1E4D37364305A01] PRIMARY KEY CLUSTERED ([cEmpId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_Unit
-- ----------------------------
ALTER TABLE [dbo].[ta_Unit] ADD CONSTRAINT [PK__ta_Unit__0D6B678A413CC257] PRIMARY KEY CLUSTERED ([cUnitId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
ON [PRIMARY]
GO

-- ----------------------------
-- Foreign Keys structure for table ta_BusinessPartner
-- ----------------------------
ALTER TABLE [dbo].[ta_BusinessPartner] ADD CONSTRAINT [FK_ta_BusinessPartner_ta_Contact] FOREIGN KEY ([cContactId]) REFERENCES [dbo].[ta_Contact] ([cContactId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

-- ----------------------------
-- Foreign Keys structure for table ta_Emp
-- ----------------------------
ALTER TABLE [dbo].[ta_Emp] ADD CONSTRAINT [FK_ta_Emp_ta_Contact] FOREIGN KEY ([cContactId]) REFERENCES [dbo].[ta_Contact] ([cContactId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO
