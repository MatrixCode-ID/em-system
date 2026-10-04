/*
    100 - Objek bisnis: produk, transaksi produk, mitra bisnis, mata uang, satuan, dan karyawan.

    Jalankan pada database target (USE [NamaDatabase]) sesudah tables/010-core.sql (ta_Contact)
    dan tables/020-approval.sql (ta_Doc). Ditujukan untuk database baru (CREATE TABLE tanpa
    pemeriksaan).

    Isi: ta_Product, ta_ProductType, ta_ProductTran, ta_ProductTranData, ta_ShoeHelper,
    ta_BusinessPartner, ta_Currency, ta_Unit, ta_Emp beserta unique, index, primary key, dan
    foreign key-nya.
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
-- Table structure for ta_Product
-- ----------------------------
CREATE TABLE [dbo].[ta_Product] (
  [cProductId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS DEFAULT [dbo].[NewUlidString]() NOT NULL,
  [cUnitId] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductTypeId] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductIdIsUlid] bit  NOT NULL,
  [cProductName] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductSku] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_Product] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_ProductTran
-- ----------------------------
CREATE TABLE [dbo].[ta_ProductTran] (
  [cProductTranId] char(34) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cDocName] varchar(25) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cBusinessPartnerId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductTranEmpRef_cEmpId] varchar(8) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cProductTranSelRef_cProductTranId] char(34) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [cProductTranRefNumber] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductTranStage] int  NOT NULL,
  [cProductTranRevision] int  NOT NULL,
  [cProductTranOrder] int  NOT NULL,
  [cProductTranDate] date  NOT NULL,
  [cProductTranNote] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL,
  [ustamp] datetime  NOT NULL,
  [datestamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_ProductTran] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_ProductTranData
-- ----------------------------
CREATE TABLE [dbo].[ta_ProductTranData] (
  [cProductTranDataId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductTranId] char(34) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cCurrencyId] varchar(25) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductTranDataOrder] int  NOT NULL,
  [cProductTranDataQty] decimal(18,2)  NOT NULL,
  [cProductTranDataAmountBase] money  NOT NULL,
  [cProductTranDataAmountModif] money  NOT NULL,
  [ustamp] datetime  NOT NULL,
  [json_object] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL
)
GO

ALTER TABLE [dbo].[ta_ProductTranData] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_ProductType
-- ----------------------------
CREATE TABLE [dbo].[ta_ProductType] (
  [cProductTypeId] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductTypeDescription] varchar(500) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_ProductType] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_ShoeHelper
-- ----------------------------
CREATE TABLE [dbo].[ta_ShoeHelper] (
  [cShoeHelperId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cProductId] char(26) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cShoeModel] char(4) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cShoeType] int  NOT NULL,
  [cShoeVersion] int  NOT NULL,
  [cShoeSize] varchar(5) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
)
GO

ALTER TABLE [dbo].[ta_ShoeHelper] SET (LOCK_ESCALATION = TABLE)
GO

-- ----------------------------
-- Table structure for ta_Unit
-- ----------------------------
CREATE TABLE [dbo].[ta_Unit] (
  [cUnitId] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUnitSymbol] varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL,
  [cUnitDesc] varchar(255) COLLATE SQL_Latin1_General_CP1_CI_AS  NOT NULL
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
-- Primary Key structure for table ta_Product
-- ----------------------------
ALTER TABLE [dbo].[ta_Product] ADD CONSTRAINT [PK__ta_Produ__C2C11E67B50DC9A7] PRIMARY KEY CLUSTERED ([cProductId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_ProductTran
-- ----------------------------
ALTER TABLE [dbo].[ta_ProductTran] ADD CONSTRAINT [PK__ta_Produ__70BAE9A9A0EDEA66] PRIMARY KEY CLUSTERED ([cProductTranId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_ProductTranData
-- ----------------------------
ALTER TABLE [dbo].[ta_ProductTranData] ADD CONSTRAINT [PK__ta_Produ__B7755D5A13E4EC34] PRIMARY KEY CLUSTERED ([cProductTranDataId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Primary Key structure for table ta_ProductType
-- ----------------------------
ALTER TABLE [dbo].[ta_ProductType] ADD CONSTRAINT [PK__ta_Produ__D10973B1C446C2A7] PRIMARY KEY CLUSTERED ([cProductTypeId])
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)  
ON [PRIMARY]
GO

-- ----------------------------
-- Indexes structure for table ta_ShoeHelper
-- ----------------------------
CREATE UNIQUE NONCLUSTERED INDEX [NonClusteredIndex-20260716-093719]
ON [dbo].[ta_ShoeHelper] (
  [cProductId] ASC
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [NonClusteredIndex-20260716-093729]
ON [dbo].[ta_ShoeHelper] (
  [cShoeModel] ASC,
  [cShoeType] ASC,
  [cShoeVersion] ASC,
  [cShoeSize] ASC
)
GO

-- ----------------------------
-- Primary Key structure for table ta_ShoeHelper
-- ----------------------------
ALTER TABLE [dbo].[ta_ShoeHelper] ADD CONSTRAINT [PK__ta_ShoeH__C2C11E678A6A45F6] PRIMARY KEY CLUSTERED ([cShoeHelperId])
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

-- ----------------------------
-- Foreign Keys structure for table ta_Product
-- ----------------------------
ALTER TABLE [dbo].[ta_Product] ADD CONSTRAINT [FK_ta_Product_ta_Unit] FOREIGN KEY ([cUnitId]) REFERENCES [dbo].[ta_Unit] ([cUnitId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

ALTER TABLE [dbo].[ta_Product] ADD CONSTRAINT [FK_ta_Product_ta_ProductType] FOREIGN KEY ([cProductTypeId]) REFERENCES [dbo].[ta_ProductType] ([cProductTypeId]) ON DELETE CASCADE ON UPDATE CASCADE
GO

-- ----------------------------
-- Foreign Keys structure for table ta_ProductTran
-- ----------------------------
ALTER TABLE [dbo].[ta_ProductTran] ADD CONSTRAINT [FK_ta_ProductTran_ta_ProductTran] FOREIGN KEY ([cProductTranSelRef_cProductTranId]) REFERENCES [dbo].[ta_ProductTran] ([cProductTranId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

ALTER TABLE [dbo].[ta_ProductTran] ADD CONSTRAINT [FK_ta_ProductTran_ta_Doc] FOREIGN KEY ([cDocName]) REFERENCES [dbo].[ta_Doc] ([cDocName]) ON DELETE CASCADE ON UPDATE CASCADE
GO

ALTER TABLE [dbo].[ta_ProductTran] ADD CONSTRAINT [FK_ta_ProductTran_ta_Emp] FOREIGN KEY ([cProductTranEmpRef_cEmpId]) REFERENCES [dbo].[ta_Emp] ([cEmpId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

ALTER TABLE [dbo].[ta_ProductTran] ADD CONSTRAINT [FK_ta_ProductTran_ta_BusinessPartner] FOREIGN KEY ([cBusinessPartnerId]) REFERENCES [dbo].[ta_BusinessPartner] ([cBusinessPartnerId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

-- ----------------------------
-- Foreign Keys structure for table ta_ProductTranData
-- ----------------------------
ALTER TABLE [dbo].[ta_ProductTranData] ADD CONSTRAINT [FK_ta_ProductTranData_ta_ProductTran] FOREIGN KEY ([cProductTranId]) REFERENCES [dbo].[ta_ProductTran] ([cProductTranId]) ON DELETE CASCADE ON UPDATE CASCADE
GO

ALTER TABLE [dbo].[ta_ProductTranData] ADD CONSTRAINT [FK_ta_ProductTranData_ta_Product] FOREIGN KEY ([cProductId]) REFERENCES [dbo].[ta_Product] ([cProductId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO

ALTER TABLE [dbo].[ta_ProductTranData] ADD CONSTRAINT [FK_ta_ProductTranData_ta_Currency] FOREIGN KEY ([cCurrencyId]) REFERENCES [dbo].[ta_Currency] ([cCurrencyId]) ON DELETE NO ACTION ON UPDATE NO ACTION
GO
