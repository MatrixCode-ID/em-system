-- NuPak module. Run on the core database after the engine identity schema.
-- Views live in views/vi_NuPak*.sql (one object per file); run them after all tables/ files.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.ta_NuPakPrefix','U') IS NOT NULL AND COL_LENGTH('dbo.ta_NuPakPrefix','cNuPakFeedId') IS NULL
 THROW 51000,'Legacy NuPak schema: run updates/20261003-NuPakMultiFeed.sql before the latest set script.',1;
IF OBJECT_ID('dbo.ta_NuPakFeed','U') IS NULL
BEGIN
 CREATE TABLE dbo.ta_NuPakFeed(
 cNuPakFeedId char(26) NOT NULL PRIMARY KEY,
 cNuPakFeedSlug varchar(64) COLLATE Latin1_General_100_CI_AS NOT NULL,
 cNuPakFeedName varchar(100) NOT NULL,
 cNuPakFeedDescription varchar(500) NULL,
 cNuPakFeedEnabled bit NOT NULL,
 cNuPakFeedAnonymousRead bit NOT NULL,
 ustamp datetime NOT NULL,datestamp datetime NOT NULL,json_object nvarchar(max) NULL,
 CONSTRAINT UQ_NuPakFeedSlug UNIQUE(cNuPakFeedSlug));
END;

IF OBJECT_ID('dbo.ta_NuPakPrefix','U') IS NULL
BEGIN
 CREATE TABLE dbo.ta_NuPakPrefix(
  cNuPakFeedId char(26) NOT NULL,
  cNuPakPrefixId char(26) NOT NULL,
  cNuPakPrefixName varchar(100) COLLATE Latin1_General_100_CI_AS NOT NULL,
  cNuPakPrefixState int NOT NULL,
  cNuPakPrefixDescription varchar(500) NULL,
  ustamp datetime NOT NULL,
  datestamp datetime NOT NULL,
  json_object nvarchar(max) NULL,
 PRIMARY KEY(cNuPakPrefixId), CONSTRAINT UQ_NuPakPrefix UNIQUE(cNuPakFeedId,cNuPakPrefixName), CONSTRAINT UQ_NuPakPrefixFeedId UNIQUE(cNuPakFeedId,cNuPakPrefixId), CONSTRAINT FK_NuPakPrefixFeed FOREIGN KEY(cNuPakFeedId) REFERENCES dbo.ta_NuPakFeed(cNuPakFeedId));
END;
IF OBJECT_ID('dbo.ta_NuPakPackage','U') IS NULL
BEGIN
 CREATE TABLE dbo.ta_NuPakPackage(
  cNuPakFeedId char(26) NOT NULL,
  cNuPakPackageId char(26) NOT NULL,
  cNuPakPrefixId char(26) NOT NULL,
  cNuPakPackageName varchar(128) COLLATE Latin1_General_100_CI_AS NOT NULL,
  cNuPakPackageState int NOT NULL,
  ustamp datetime NOT NULL,
  datestamp datetime NOT NULL,
  json_object nvarchar(max) NULL,
 PRIMARY KEY(cNuPakPackageId), CONSTRAINT UQ_NuPakPackage UNIQUE(cNuPakFeedId,cNuPakPackageName), CONSTRAINT FK_NuPakPackagePrefixFeed FOREIGN KEY(cNuPakFeedId,cNuPakPrefixId) REFERENCES dbo.ta_NuPakPrefix(cNuPakFeedId,cNuPakPrefixId), CONSTRAINT FK_NuPakPackageFeed FOREIGN KEY(cNuPakFeedId) REFERENCES dbo.ta_NuPakFeed(cNuPakFeedId));
 CREATE INDEX IX_NuPakPackagePrefix ON dbo.ta_NuPakPackage(cNuPakPrefixId);
END;
IF OBJECT_ID('dbo.ta_NuPakVersion','U') IS NULL
BEGIN
 CREATE TABLE dbo.ta_NuPakVersion(
  cNuPakVersionId char(26) NOT NULL,
  cNuPakPackageId char(26) NOT NULL,
  cNuPakVersionNumber varchar(64) NOT NULL,
  cNuPakVersionOriginal varchar(64) NOT NULL,
  cNuPakVersionPrerelease bit NOT NULL,
  cNuPakVersionState int NOT NULL,
  cNuPakVersionRecycledAt datetime NULL,
  cNuPakVersionSize bigint NOT NULL,
  cNuPakVersionHash varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
  cNuPakVersionNuspec nvarchar(max) NOT NULL,
  cNuPakVersionTitle varchar(255) NULL,
  cNuPakVersionDescription varchar(4000) NULL,
  cNuPakVersionAuthors varchar(500) NULL,
  cNuPakVersionTags varchar(1000) NULL,
  cNuPakVersionPushedBy_cRobotId char(26) NULL,
  ustamp datetime NOT NULL,
  datestamp datetime NOT NULL,
  json_object nvarchar(max) NULL,
 PRIMARY KEY(cNuPakVersionId), CONSTRAINT UQ_NuPakVersion UNIQUE(cNuPakPackageId,cNuPakVersionNumber), FOREIGN KEY(cNuPakPackageId) REFERENCES dbo.ta_NuPakPackage(cNuPakPackageId) ON DELETE CASCADE, FOREIGN KEY(cNuPakVersionPushedBy_cRobotId) REFERENCES dbo.ta_Robot(cRobotId));
 CREATE INDEX IX_NuPakVersionState ON dbo.ta_NuPakVersion(cNuPakVersionState,cNuPakPackageId) INCLUDE(cNuPakVersionSize);
END;
IF OBJECT_ID('dbo.ta_NuPakPrefixRobot','U') IS NULL
BEGIN
 CREATE TABLE dbo.ta_NuPakPrefixRobot(
  cRobotId char(26) NOT NULL,
  cNuPakPrefixId char(26) NOT NULL,
  cNuPakPrefixRobotAccess varchar(8) NOT NULL,
  ustamp datetime NOT NULL,
  datestamp datetime NOT NULL,
  json_object nvarchar(max) NULL,
 PRIMARY KEY(cRobotId,cNuPakPrefixId), FOREIGN KEY(cNuPakPrefixId) REFERENCES dbo.ta_NuPakPrefix(cNuPakPrefixId) ON DELETE CASCADE, FOREIGN KEY(cRobotId) REFERENCES dbo.ta_Robot(cRobotId), CHECK(cNuPakPrefixRobotAccess IN ('R','W')));
END;
IF OBJECT_ID('dbo.ta_NuPakAudit','U') IS NULL
BEGIN
 CREATE TABLE dbo.ta_NuPakAudit(
  cNuPakFeedId char(26) NULL,
  cNuPakAuditFeedSlug varchar(64) NULL,
  cNuPakAuditFeedName varchar(100) NULL,
  cNuPakAuditId char(26) NOT NULL,
  cNuPakAuditAt datetime NOT NULL,
  cNuPakAuditAction varchar(20) NOT NULL,
  cNuPakAuditPackage varchar(128) NULL,
  cNuPakAuditVersion varchar(64) NULL,
  cNuPakAuditActorKind varchar(10) NOT NULL,
  cNuPakAuditActorId char(26) NULL,
  cNuPakAuditActorName varchar(255) NOT NULL,
  cNuPakAuditResult varchar(10) NOT NULL,
  cNuPakAuditDetail varchar(500) NULL,
  cNuPakAuditAddress varchar(64) NULL,
  ustamp datetime NOT NULL,
  datestamp datetime NOT NULL,
  json_object nvarchar(max) NULL,
 PRIMARY KEY(cNuPakAuditId), CONSTRAINT FK_NuPakAuditFeed FOREIGN KEY(cNuPakFeedId) REFERENCES dbo.ta_NuPakFeed(cNuPakFeedId), CHECK(cNuPakAuditResult IN ('Success','Denied','Failed')));
 CREATE INDEX IX_NuPakAuditAt ON dbo.ta_NuPakAudit(cNuPakAuditAt DESC);
 CREATE INDEX IX_NuPakAuditPackage ON dbo.ta_NuPakAudit(cNuPakAuditPackage,cNuPakAuditVersion);
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_NuPakAuditFeedAt' AND object_id=OBJECT_ID('dbo.ta_NuPakAudit'))
 CREATE INDEX IX_NuPakAuditFeedAt ON dbo.ta_NuPakAudit(cNuPakFeedId,cNuPakAuditAt DESC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_NuPakVersionRecycle' AND object_id=OBJECT_ID('dbo.ta_NuPakVersion'))
 CREATE INDEX IX_NuPakVersionRecycle ON dbo.ta_NuPakVersion(cNuPakVersionState,cNuPakVersionRecycledAt,cNuPakPackageId) INCLUDE(cNuPakVersionSize);
IF NOT EXISTS(SELECT 1 FROM dbo.ta_Meta WHERE cMetaKey='NuPakSchemaVersion')
 INSERT dbo.ta_Meta(cMetaKey,cMetaValue,cMetaDescription,ustamp) VALUES('NuPakSchemaVersion','2','NuPak multi-feed schema',GETUTCDATE());
ELSE UPDATE dbo.ta_Meta SET cMetaValue='2',ustamp=GETUTCDATE() WHERE cMetaKey='NuPakSchemaVersion';
COMMIT;
