/* Reject legacy single-feed tables before making any changes. */
DROP PROCEDURE IF EXISTS mysql_check_nupak_version;
DELIMITER $$
CREATE PROCEDURE mysql_check_nupak_version()
BEGIN
 IF EXISTS(SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakPrefix')
 AND NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakPrefix' AND COLUMN_NAME='cNuPakFeedId') THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Legacy NuPak schema: run updates/20261003-NuPakMultiFeed.sql first';
 END IF;
END$$
DELIMITER ;
CALL mysql_check_nupak_version();
DROP PROCEDURE mysql_check_nupak_version;

/* MySQL 8.0+ application schema. Install after its core dependencies. */

CREATE TABLE IF NOT EXISTS ta_NuPakFeed(
 cNuPakFeedId char(26) NOT NULL PRIMARY KEY,
 cNuPakFeedSlug varchar(64) COLLATE utf8mb4_0900_ai_ci NOT NULL,
 cNuPakFeedName varchar(100) NOT NULL,
 cNuPakFeedDescription varchar(500) NULL,
 cNuPakFeedEnabled bit NOT NULL,
 cNuPakFeedAnonymousRead bit NOT NULL,
 ustamp datetime(3) NOT NULL,datestamp datetime(3) NOT NULL,json_object longtext NULL,
 CONSTRAINT UQ_NuPakFeedSlug UNIQUE(cNuPakFeedSlug)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS ta_NuPakPrefix(
  cNuPakFeedId char(26) NOT NULL,
  cNuPakPrefixId char(26) NOT NULL,
  cNuPakPrefixName varchar(100) COLLATE utf8mb4_0900_ai_ci NOT NULL,
  cNuPakPrefixState int NOT NULL,
  cNuPakPrefixDescription varchar(500) NULL,
  ustamp datetime(3) NOT NULL,
  datestamp datetime(3) NOT NULL,
  json_object longtext NULL,
 PRIMARY KEY(cNuPakPrefixId), CONSTRAINT UQ_NuPakPrefix UNIQUE(cNuPakFeedId,cNuPakPrefixName), CONSTRAINT UQ_NuPakPrefixFeedId UNIQUE(cNuPakFeedId,cNuPakPrefixId), CONSTRAINT FK_NuPakPrefixFeed FOREIGN KEY(cNuPakFeedId) REFERENCES ta_NuPakFeed(cNuPakFeedId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS ta_NuPakPackage(
  cNuPakFeedId char(26) NOT NULL,
  cNuPakPackageId char(26) NOT NULL,
  cNuPakPrefixId char(26) NOT NULL,
  cNuPakPackageName varchar(128) COLLATE utf8mb4_0900_ai_ci NOT NULL,
  cNuPakPackageState int NOT NULL,
  ustamp datetime(3) NOT NULL,
  datestamp datetime(3) NOT NULL,
  json_object longtext NULL,
 PRIMARY KEY(cNuPakPackageId), CONSTRAINT UQ_NuPakPackage UNIQUE(cNuPakFeedId,cNuPakPackageName), CONSTRAINT FK_NuPakPackagePrefixFeed FOREIGN KEY(cNuPakFeedId,cNuPakPrefixId) REFERENCES ta_NuPakPrefix(cNuPakFeedId,cNuPakPrefixId), CONSTRAINT FK_NuPakPackageFeed FOREIGN KEY(cNuPakFeedId) REFERENCES ta_NuPakFeed(cNuPakFeedId),
   KEY `IX_NuPakPackagePrefix` (cNuPakPrefixId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS ta_NuPakVersion(
  cNuPakVersionId char(26) NOT NULL,
  cNuPakPackageId char(26) NOT NULL,
  cNuPakVersionNumber varchar(64) NOT NULL,
  cNuPakVersionOriginal varchar(64) NOT NULL,
  cNuPakVersionPrerelease bit NOT NULL,
  cNuPakVersionState int NOT NULL,
  cNuPakVersionRecycledAt datetime(3) NULL,
  cNuPakVersionSize bigint NOT NULL,
  cNuPakVersionHash varchar(128) COLLATE utf8mb4_0900_as_cs NOT NULL,
  cNuPakVersionNuspec longtext NOT NULL,
  cNuPakVersionTitle varchar(255) NULL,
  cNuPakVersionDescription varchar(4000) NULL,
  cNuPakVersionAuthors varchar(500) NULL,
  cNuPakVersionTags varchar(1000) NULL,
  cNuPakVersionPushedBy_cRobotId char(26) NULL,
  ustamp datetime(3) NOT NULL,
  datestamp datetime(3) NOT NULL,
  json_object longtext NULL,
 PRIMARY KEY(cNuPakVersionId), CONSTRAINT UQ_NuPakVersion UNIQUE(cNuPakPackageId,cNuPakVersionNumber), FOREIGN KEY(cNuPakPackageId) REFERENCES ta_NuPakPackage(cNuPakPackageId) ON DELETE CASCADE, FOREIGN KEY(cNuPakVersionPushedBy_cRobotId) REFERENCES ta_Robot(cRobotId),
   KEY `IX_NuPakVersionState` (cNuPakVersionState,cNuPakPackageId),
   KEY `IX_NuPakVersionRecycle` (cNuPakVersionState,cNuPakVersionRecycledAt,cNuPakPackageId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS ta_NuPakPrefixRobot(
  cRobotId char(26) NOT NULL,
  cNuPakPrefixId char(26) NOT NULL,
  cNuPakPrefixRobotAccess varchar(8) NOT NULL,
  ustamp datetime(3) NOT NULL,
  datestamp datetime(3) NOT NULL,
  json_object longtext NULL,
 PRIMARY KEY(cRobotId,cNuPakPrefixId), FOREIGN KEY(cNuPakPrefixId) REFERENCES ta_NuPakPrefix(cNuPakPrefixId) ON DELETE CASCADE, FOREIGN KEY(cRobotId) REFERENCES ta_Robot(cRobotId), CHECK(cNuPakPrefixRobotAccess IN ('R','W'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS ta_NuPakAudit(
  cNuPakFeedId char(26) NULL,
  cNuPakAuditFeedSlug varchar(64) NULL,
  cNuPakAuditFeedName varchar(100) NULL,
  cNuPakAuditId char(26) NOT NULL,
  cNuPakAuditAt datetime(3) NOT NULL,
  cNuPakAuditAction varchar(20) NOT NULL,
  cNuPakAuditPackage varchar(128) NULL,
  cNuPakAuditVersion varchar(64) NULL,
  cNuPakAuditActorKind varchar(10) NOT NULL,
  cNuPakAuditActorId char(26) NULL,
  cNuPakAuditActorName varchar(255) NOT NULL,
  cNuPakAuditResult varchar(10) NOT NULL,
  cNuPakAuditDetail varchar(500) NULL,
  cNuPakAuditAddress varchar(64) NULL,
  ustamp datetime(3) NOT NULL,
  datestamp datetime(3) NOT NULL,
  json_object longtext NULL,
 PRIMARY KEY(cNuPakAuditId), CONSTRAINT FK_NuPakAuditFeed FOREIGN KEY(cNuPakFeedId) REFERENCES ta_NuPakFeed(cNuPakFeedId), CHECK(cNuPakAuditResult IN ('Success','Denied','Failed')),
   KEY `IX_NuPakAuditAt` (cNuPakAuditAt DESC),
   KEY `IX_NuPakAuditPackage` (cNuPakAuditPackage,cNuPakAuditVersion),
   KEY `IX_NuPakAuditFeedAt` (cNuPakFeedId,cNuPakAuditAt DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO ta_Meta (cMetaKey,cMetaValue,cMetaDescription,ustamp) VALUES ('NuPakSchemaVersion','2','NuPak multi-feed schema',UTC_TIMESTAMP(3)) ON DUPLICATE KEY UPDATE cMetaValue='2',ustamp=UTC_TIMESTAMP(3);
