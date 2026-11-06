/* Upgrade an EMPTY legacy single-feed NuPak installation. Populated legacy feeds
   require manual allocation and are rejected. MySQL DDL implicitly commits: back
   up first; a failure can require manual repair. Install views after this script. */
DROP PROCEDURE IF EXISTS mysql_upgrade_nupak_multifeed;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_nupak_multifeed()
BEGIN
 DECLARE done int DEFAULT 0; DECLARE fk varchar(64);
 DECLARE cur CURSOR FOR SELECT DISTINCT CONSTRAINT_NAME FROM information_schema.KEY_COLUMN_USAGE
  WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakPackage' AND REFERENCED_TABLE_NAME='ta_NuPakPrefix';
 DECLARE CONTINUE HANDLER FOR NOT FOUND SET done=1;
 IF NOT EXISTS(SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakPrefix') THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='NuPak is not installed; use tables/040-nupak.sql';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakPrefix' AND COLUMN_NAME='cNuPakFeedId') THEN
  IF EXISTS(SELECT 1 FROM ta_NuPakPrefix) OR EXISTS(SELECT 1 FROM ta_NuPakPackage) OR EXISTS(SELECT 1 FROM ta_NuPakVersion) OR EXISTS(SELECT 1 FROM ta_NuPakPrefixRobot) THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Populated legacy NuPak requires manual feed allocation';
  END IF;
  IF NOT EXISTS(SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakPrefix' AND INDEX_NAME='UQ_NuPakPrefix') OR
     NOT EXISTS(SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakPackage' AND INDEX_NAME='UQ_NuPakPackage') THEN
   SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Legacy NuPak indexes differ; inspect before upgrade';
  END IF;
  CREATE TABLE IF NOT EXISTS ta_NuPakFeed (
   cNuPakFeedId char(26) NOT NULL PRIMARY KEY, cNuPakFeedSlug varchar(64) NOT NULL,
   cNuPakFeedName varchar(100) NOT NULL, cNuPakFeedDescription varchar(500) NULL,
   cNuPakFeedEnabled bit NOT NULL, cNuPakFeedAnonymousRead bit NOT NULL,
   ustamp datetime(3) NOT NULL, datestamp datetime(3) NOT NULL, json_object longtext NULL,
   CONSTRAINT UQ_NuPakFeedSlug UNIQUE(cNuPakFeedSlug)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
  OPEN cur;
  fk_loop: LOOP
   FETCH cur INTO fk; IF done=1 THEN LEAVE fk_loop; END IF;
   SET @mysql_upgrade_sql=CONCAT('ALTER TABLE ta_NuPakPackage DROP FOREIGN KEY `',REPLACE(fk,'`','``'),'`');
   PREPARE stmt FROM @mysql_upgrade_sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END LOOP;
  CLOSE cur;
  ALTER TABLE ta_NuPakPrefix ADD cNuPakFeedId char(26) NOT NULL, DROP INDEX UQ_NuPakPrefix,
   ADD CONSTRAINT UQ_NuPakPrefix UNIQUE(cNuPakFeedId,cNuPakPrefixName),
   ADD CONSTRAINT UQ_NuPakPrefixFeedId UNIQUE(cNuPakFeedId,cNuPakPrefixId),
   ADD CONSTRAINT FK_NuPakPrefixFeed FOREIGN KEY(cNuPakFeedId) REFERENCES ta_NuPakFeed(cNuPakFeedId);
  ALTER TABLE ta_NuPakPackage ADD cNuPakFeedId char(26) NOT NULL, DROP INDEX UQ_NuPakPackage,
   ADD CONSTRAINT UQ_NuPakPackage UNIQUE(cNuPakFeedId,cNuPakPackageName),
   ADD CONSTRAINT FK_NuPakPackageFeed FOREIGN KEY(cNuPakFeedId) REFERENCES ta_NuPakFeed(cNuPakFeedId),
   ADD CONSTRAINT FK_NuPakPackagePrefixFeed FOREIGN KEY(cNuPakFeedId,cNuPakPrefixId) REFERENCES ta_NuPakPrefix(cNuPakFeedId,cNuPakPrefixId);
  ALTER TABLE ta_NuPakAudit ADD cNuPakFeedId char(26) NULL, ADD cNuPakAuditFeedSlug varchar(64) NULL, ADD cNuPakAuditFeedName varchar(100) NULL,
   ADD CONSTRAINT FK_NuPakAuditFeed FOREIGN KEY(cNuPakFeedId) REFERENCES ta_NuPakFeed(cNuPakFeedId);
  UPDATE ta_NuPakAudit SET cNuPakAuditFeedName='Legacy single-feed history';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakAudit' AND INDEX_NAME='IX_NuPakAuditFeedAt') THEN
  CREATE INDEX IX_NuPakAuditFeedAt ON ta_NuPakAudit(cNuPakFeedId,cNuPakAuditAt DESC);
 END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_NuPakVersion' AND INDEX_NAME='IX_NuPakVersionRecycle') THEN
  CREATE INDEX IX_NuPakVersionRecycle ON ta_NuPakVersion(cNuPakVersionState,cNuPakVersionRecycledAt,cNuPakPackageId);
 END IF;
 INSERT INTO ta_Meta(cMetaKey,cMetaValue,cMetaDescription,ustamp) VALUES('NuPakSchemaVersion','2','NuPak multi-feed schema',UTC_TIMESTAMP(3))
 ON DUPLICATE KEY UPDATE cMetaValue='2',ustamp=UTC_TIMESTAMP(3);
END$$
DELIMITER ;
CALL mysql_upgrade_nupak_multifeed();
DROP PROCEDURE mysql_upgrade_nupak_multifeed;
