/* DESTRUCTIVE for approval v1: old requests are discarded; v2 rows are retained.
   MySQL DDL commits implicitly. Stop the application and back up before running.
   External foreign keys into the old approval tables block this migration. */
DROP PROCEDURE IF EXISTS mysql_upgrade_approval_v2;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_approval_v2()
BEGIN
 DECLARE done int DEFAULT 0; DECLARE tbl varchar(64); DECLARE fk varchar(64);
 DECLARE cur CURSOR FOR SELECT TABLE_NAME,CONSTRAINT_NAME FROM information_schema.REFERENTIAL_CONSTRAINTS
  WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME IN ('ta_ApprovalRequest','ta_ApprovalRequestItem','ta_ApprovalRequestItemField','ta_ApprovalRequestStep','ta_ApprovalRequestStepCandidate','ta_ApprovalRequestSignature','ta_ApprovalRequestComment','ta_ApprovalRequestAttachment','ta_ApprovalSignerSetting','ta_ApprovalSignerSettingBackup','ta_ApprovalRequestStepSigner','ta_ApprovalRequestItemKey');
 DECLARE CONTINUE HANDLER FOR NOT FOUND SET done=1;
 IF EXISTS(SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_ApprovalRequest')
  AND NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_ApprovalRequest' AND COLUMN_NAME='cApprovalName') THEN
  IF EXISTS(SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE()
   AND REFERENCED_TABLE_NAME IN ('ta_ApprovalRequest','ta_ApprovalRequestItem','ta_ApprovalRequestItemField','ta_ApprovalRequestStep','ta_ApprovalRequestStepCandidate','ta_ApprovalRequestSignature','ta_ApprovalRequestComment','ta_ApprovalRequestAttachment','ta_ApprovalSignerSetting','ta_ApprovalSignerSettingBackup','ta_ApprovalRequestStepSigner','ta_ApprovalRequestItemKey')
   AND TABLE_NAME NOT IN ('ta_ApprovalRequest','ta_ApprovalRequestItem','ta_ApprovalRequestItemField','ta_ApprovalRequestStep','ta_ApprovalRequestStepCandidate','ta_ApprovalRequestSignature','ta_ApprovalRequestComment','ta_ApprovalRequestAttachment','ta_ApprovalSignerSetting','ta_ApprovalSignerSettingBackup','ta_ApprovalRequestStepSigner','ta_ApprovalRequestItemKey')) THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='External foreign keys reference approval tables; reconcile before upgrade';
  END IF;
  OPEN cur;
  drop_loop: LOOP
   FETCH cur INTO tbl,fk; IF done=1 THEN LEAVE drop_loop; END IF;
   SET @mysql_upgrade_sql=CONCAT('ALTER TABLE `',REPLACE(tbl,'`','``'),'` DROP FOREIGN KEY `',REPLACE(fk,'`','``'),'`');
   PREPARE stmt FROM @mysql_upgrade_sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END LOOP;
  CLOSE cur;
  DROP TABLE IF EXISTS ta_ApprovalRequestItemKey,ta_ApprovalRequestStepSigner,ta_ApprovalSignerSettingBackup,ta_ApprovalSignerSetting,ta_ApprovalRequestAttachment,ta_ApprovalRequestComment,ta_ApprovalRequestSignature,ta_ApprovalRequestStepCandidate,ta_ApprovalRequestStep,ta_ApprovalRequestItemField,ta_ApprovalRequestItem,ta_ApprovalRequest;
 END IF;
END$$
DELIMITER ;
CALL mysql_upgrade_approval_v2();
DROP PROCEDURE mysql_upgrade_approval_v2;

/* Approval schema v2 for MySQL 8.0+. Requires tables/010-core.sql.
   Run on a new database. Existing v1 data requires a dedicated upgrade. */

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequest` (
   `cApprovalRequestId` char(26) NOT NULL,
   `cApprovalRequestKind` tinyint unsigned NOT NULL,
   `cApprovalName` varchar(50) NOT NULL,
   `cSubjectKey` varchar(400) NULL,
   `cSummary` varchar(400) NOT NULL,
   `cSubSummary` varchar(400) NULL,
   `cApprovalRequestRequesterId` char(26) NOT NULL,
   `cApprovalRequestStage` int NOT NULL,
   `cApprovalRequestLevel` int NOT NULL,
   `cApprovalRequestCompletedDate` datetime(6) NULL,
   `cReinstateOf` char(26) NULL,
   `cApprovalRequestNote` varchar(1000) NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequest` PRIMARY KEY (`cApprovalRequestId`),
   CONSTRAINT `FK_ApprovalRequest_0` FOREIGN KEY (`cApprovalRequestRequesterId`) REFERENCES `ta_User` (`cUserId`),
   CONSTRAINT `FK_ApprovalRequest_1` FOREIGN KEY (`cReinstateOf`) REFERENCES `ta_ApprovalRequest` (`cApprovalRequestId`)
,
   `mysql_active_subject` varchar(400) GENERATED ALWAYS AS (CASE WHEN `cApprovalRequestKind` = 2 AND `cApprovalRequestStage` >= -1 THEN COALESCE(`cSubjectKey`, '') ELSE NULL END) STORED,
   `mysql_active_subject_is_null` tinyint GENERATED ALWAYS AS (CASE WHEN `cApprovalRequestKind` = 2 AND `cApprovalRequestStage` >= -1 THEN (`cSubjectKey` IS NULL) ELSE NULL END) STORED,
   UNIQUE KEY `UX_ApprovalSystemActive` (`cApprovalRequestKind`, `cApprovalName`, `mysql_active_subject_is_null`, `mysql_active_subject`),
   KEY `IX_ApprovalSubject` (cApprovalRequestKind,cApprovalName,cSubjectKey)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequestItem` (
   `cApprovalRequestItemId` char(26) NOT NULL,
   `cApprovalRequestId` char(26) NOT NULL,
   `cApprovalRequestItemOrder` int NOT NULL,
   `cApprovalRequestItemEntity` varchar(400) NOT NULL,
   `cApprovalRequestItemOperation` int NOT NULL,
   `cSubjectKey` varchar(400) NULL,
   `cParentItem` char(26) NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequestItem` PRIMARY KEY (`cApprovalRequestItemId`),
   CONSTRAINT `FK_ApprovalRequestItem_0` FOREIGN KEY (`cApprovalRequestId`) REFERENCES `ta_ApprovalRequest` (`cApprovalRequestId`),
   CONSTRAINT `FK_ApprovalRequestItem_1` FOREIGN KEY (`cParentItem`) REFERENCES `ta_ApprovalRequestItem` (`cApprovalRequestItemId`)

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequestItemField` (
   `cApprovalRequestItemFieldId` char(26) NOT NULL,
   `cApprovalRequestItemId` char(26) NOT NULL,
   `cApprovalRequestItemFieldName` varchar(128) NOT NULL,
   `cApprovalRequestItemFieldOld` longtext NULL,
   `cApprovalRequestItemFieldNew` longtext NULL,
   `cApprovalRequestItemFieldCurrent` longtext NULL,
   `cApprovalRequestItemFieldConflict` bit NOT NULL,
   `cApprovalRequestItemFieldOverrideReason` varchar(1000) NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequestItemField` PRIMARY KEY (`cApprovalRequestItemFieldId`),
   CONSTRAINT `FK_ApprovalRequestItemField_0` FOREIGN KEY (`cApprovalRequestItemId`) REFERENCES `ta_ApprovalRequestItem` (`cApprovalRequestItemId`)
,
   UNIQUE KEY `UX_ApprovalField` (`cApprovalRequestItemId`, `cApprovalRequestItemFieldName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequestStep` (
   `cApprovalRequestStepId` char(26) NOT NULL,
   `cApprovalRequestId` char(26) NOT NULL,
   `cApprovalRequestStepLevel` int NOT NULL,
   `cApprovalRequestStepOrder` int NOT NULL,
   `cApprovalRequestStepName` varchar(100) NOT NULL,
   `cApprovalRequestStepKind` int NOT NULL,
   `cApprovalRequestStepStage` int NOT NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequestStep` PRIMARY KEY (`cApprovalRequestStepId`),
   CONSTRAINT `FK_ApprovalRequestStep_0` FOREIGN KEY (`cApprovalRequestId`) REFERENCES `ta_ApprovalRequest` (`cApprovalRequestId`)
,
   UNIQUE KEY `UX_ApprovalStepName` (`cApprovalRequestId`, `cApprovalRequestStepName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequestStepCandidate` (
   `cApprovalRequestStepCandidateId` char(26) NOT NULL,
   `cApprovalRequestStepId` char(26) NOT NULL,
   `cUserId` char(26) NOT NULL,
   `cApprovalRequestStepCandidateBackup` bit NOT NULL,
   `cApprovalRequestStepCandidateOrder` int NOT NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequestStepCandidate` PRIMARY KEY (`cApprovalRequestStepCandidateId`),
   CONSTRAINT `FK_ApprovalRequestStepCandidate_0` FOREIGN KEY (`cApprovalRequestStepId`) REFERENCES `ta_ApprovalRequestStep` (`cApprovalRequestStepId`),
   CONSTRAINT `FK_ApprovalRequestStepCandidate_1` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`)
,
   UNIQUE KEY `UX_ApprovalStepCandidate` (`cApprovalRequestStepId`, `cUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequestSignature` (
   `cApprovalRequestSignatureId` char(26) NOT NULL,
   `cApprovalRequestId` char(26) NOT NULL,
   `cApprovalRequestStepId` char(26) NULL,
   `cUserId` char(26) NOT NULL,
   `cApprovalRequestSignatureApproved` bit NOT NULL,
   `cApprovalRequestSignatureNote` varchar(1000) NULL,
   `cApprovalRequestSignatureCode` varchar(40) NOT NULL,
   `cApprovalRequestSignatureAdmin` bit NOT NULL,
   `cApprovalRequestSignatureBackup` bit NOT NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequestSignature` PRIMARY KEY (`cApprovalRequestSignatureId`),
   CONSTRAINT `FK_ApprovalRequestSignature_0` FOREIGN KEY (`cApprovalRequestId`) REFERENCES `ta_ApprovalRequest` (`cApprovalRequestId`),
   CONSTRAINT `FK_ApprovalRequestSignature_1` FOREIGN KEY (`cApprovalRequestStepId`) REFERENCES `ta_ApprovalRequestStep` (`cApprovalRequestStepId`),
   CONSTRAINT `FK_ApprovalRequestSignature_2` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`)
,
   UNIQUE KEY `UX_ApprovalSignature` (`cApprovalRequestStepId`),
   UNIQUE KEY `UX_ApprovalVerification` (`cApprovalRequestSignatureCode`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequestComment` (
   `cApprovalRequestCommentId` char(26) NOT NULL,
   `cApprovalRequestId` char(26) NOT NULL,
   `cUserId` char(26) NOT NULL,
   `cApprovalRequestCommentText` varchar(1000) NOT NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequestComment` PRIMARY KEY (`cApprovalRequestCommentId`),
   CONSTRAINT `FK_ApprovalRequestComment_0` FOREIGN KEY (`cApprovalRequestId`) REFERENCES `ta_ApprovalRequest` (`cApprovalRequestId`)

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalRequestAttachment` (
   `cApprovalRequestAttachmentId` char(26) NOT NULL,
   `cApprovalRequestId` char(26) NOT NULL,
   `cApprovalRequestCommentId` char(26) NULL,
   `cApprovalRequestAttachmentKey` varchar(400) NOT NULL,
   `cApprovalRequestAttachmentVersion` int NOT NULL,
   `cApprovalRequestAttachmentName` varchar(255) NOT NULL,
   `cApprovalRequestAttachmentSize` bigint NOT NULL,
   `cUserId` char(26) NOT NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalRequestAttachment` PRIMARY KEY (`cApprovalRequestAttachmentId`),
   CONSTRAINT `FK_ApprovalRequestAttachment_0` FOREIGN KEY (`cApprovalRequestId`) REFERENCES `ta_ApprovalRequest` (`cApprovalRequestId`),
   CONSTRAINT `FK_ApprovalRequestAttachment_1` FOREIGN KEY (`cApprovalRequestCommentId`) REFERENCES `ta_ApprovalRequestComment` (`cApprovalRequestCommentId`),
   CONSTRAINT `FK_ApprovalRequestAttachment_2` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`)

) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalSignerSetting` (
   `cApprovalSignerSettingId` char(26) NOT NULL,
   `cApprovalName` varchar(50) NOT NULL,
   `cApprovalSignerSettingStep` varchar(100) NOT NULL,
   `cApprovalSignerSettingDynamic` bit NOT NULL,
   `cUserId` char(26) NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalSignerSetting` PRIMARY KEY (`cApprovalSignerSettingId`),
   CONSTRAINT `FK_ApprovalSignerSetting_0` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`)
,
   UNIQUE KEY `UX_ApprovalSetting` (`cApprovalName`, `cApprovalSignerSettingStep`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_ApprovalSignerSettingBackup` (
   `cApprovalSignerSettingBackupId` char(26) NOT NULL,
   `cApprovalSignerSettingId` char(26) NOT NULL,
   `cUserId` char(26) NOT NULL,
   `cApprovalSignerSettingBackupOrder` int NOT NULL,
   `ustamp` datetime(6) NOT NULL,
   `datestamp` datetime(6) NOT NULL,
   `json_object` longtext NULL,
   CONSTRAINT `PK_ta_ApprovalSignerSettingBackup` PRIMARY KEY (`cApprovalSignerSettingBackupId`),
   CONSTRAINT `FK_ApprovalSignerSettingBackup_0` FOREIGN KEY (`cApprovalSignerSettingId`) REFERENCES `ta_ApprovalSignerSetting` (`cApprovalSignerSettingId`),
   CONSTRAINT `FK_ApprovalSignerSettingBackup_1` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`)
,
   UNIQUE KEY `UX_ApprovalSettingBackup` (`cApprovalSignerSettingId`, `cUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

