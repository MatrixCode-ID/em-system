/* Adds tenant positions and contact attachments to an installation that already has tables/090-tenant-org.sql, and
   replaces the employee position text with a reference to a position. Text values that exist become positions of
   their tenant before the column is removed, with the first 50 characters as the code. Safe to run again. MySQL DDL
   commits implicitly, so take a backup first. Run views/vi_TenantPosition.sql and views/vi_TenantEmp.sql afterwards. */
CREATE TABLE IF NOT EXISTS ta_TenantPosition (
 cTenantPositionId char(26) NOT NULL, cTenantId char(26) NOT NULL, cTenantPositionCode varchar(50) NOT NULL,
 cTenantPositionName varchar(255) NOT NULL, cTenantPositionState int NOT NULL DEFAULT 0, cTenantPositionNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantPositionId), UNIQUE KEY UQ_ta_TenantPosition_cTenantId_cTenantPositionCode(cTenantId,cTenantPositionCode),
 UNIQUE KEY UQ_ta_TenantPosition_cTenantPositionId_cTenantId(cTenantPositionId,cTenantId),
 CONSTRAINT FK_ta_TenantPosition_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_ContactAttachment (
 cContactAttachmentId char(26) NOT NULL, cContactId char(26) NOT NULL, cStoreObjectId char(26) NOT NULL,
 cContactAttachmentName varchar(255) NOT NULL, cContactAttachmentKind int NOT NULL DEFAULT 99, cContactAttachmentNote varchar(500) NULL,
 cContactAttachmentState int NOT NULL DEFAULT 0, cContactAttachmentOrder int NOT NULL DEFAULT 0,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cContactAttachmentId), KEY IX_ta_ContactAttachment_cContactId(cContactId),
 CONSTRAINT FK_ta_ContactAttachment_ta_Contact FOREIGN KEY(cContactId) REFERENCES ta_Contact(cContactId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
DROP PROCEDURE IF EXISTS mysql_upgrade_tenant_position;
DELIMITER $$
CREATE PROCEDURE mysql_upgrade_tenant_position()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_TenantEmp') THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Install tables/090-tenant-org.sql first';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_TenantEmp' AND COLUMN_NAME='cTenantPositionId') THEN
  ALTER TABLE ta_TenantEmp ADD cTenantPositionId char(26) NULL;
 END IF;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_TenantEmp' AND COLUMN_NAME='cTenantEmpPosition') THEN
  SET @move_positions = "INSERT INTO ta_TenantPosition (cTenantPositionId, cTenantId, cTenantPositionCode, cTenantPositionName)
   SELECT NewUlidString(), g.cTenantId, g.code, g.name FROM (
    SELECT e.cTenantId, LEFT(TRIM(e.cTenantEmpPosition), 50) AS code, MIN(TRIM(e.cTenantEmpPosition)) AS name
    FROM ta_TenantEmp e WHERE TRIM(IFNULL(e.cTenantEmpPosition, '')) <> ''
    GROUP BY e.cTenantId, LEFT(TRIM(e.cTenantEmpPosition), 50)) g
   WHERE NOT EXISTS (SELECT 1 FROM ta_TenantPosition p WHERE p.cTenantId = g.cTenantId AND p.cTenantPositionCode = g.code)";
  PREPARE statement FROM @move_positions; EXECUTE statement; DEALLOCATE PREPARE statement;
  SET @link_positions = "UPDATE ta_TenantEmp e JOIN ta_TenantPosition p ON p.cTenantId = e.cTenantId AND p.cTenantPositionCode = LEFT(TRIM(e.cTenantEmpPosition), 50)
   SET e.cTenantPositionId = p.cTenantPositionId WHERE e.cTenantPositionId IS NULL AND TRIM(IFNULL(e.cTenantEmpPosition, '')) <> ''";
  PREPARE statement FROM @link_positions; EXECUTE statement; DEALLOCATE PREPARE statement;
  ALTER TABLE ta_TenantEmp DROP COLUMN cTenantEmpPosition;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='FK_ta_TenantEmp_ta_TenantPosition') THEN
  ALTER TABLE ta_TenantEmp ADD CONSTRAINT FK_ta_TenantEmp_ta_TenantPosition FOREIGN KEY(cTenantPositionId,cTenantId) REFERENCES ta_TenantPosition(cTenantPositionId,cTenantId);
 END IF;
END$$
DELIMITER ;
CALL mysql_upgrade_tenant_position();
DROP PROCEDURE mysql_upgrade_tenant_position;
DROP TRIGGER IF EXISTS tr_ta_TenantPosition_ulid;
DROP TRIGGER IF EXISTS tr_ta_ContactAttachment_ulid;
DELIMITER $$
CREATE TRIGGER tr_ta_TenantPosition_ulid BEFORE INSERT ON ta_TenantPosition FOR EACH ROW
BEGIN
 IF NEW.cTenantPositionId IS NULL OR NEW.cTenantPositionId = '' THEN SET NEW.cTenantPositionId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_ContactAttachment_ulid BEFORE INSERT ON ta_ContactAttachment FOR EACH ROW
BEGIN
 IF NEW.cContactAttachmentId IS NULL OR NEW.cContactAttachmentId = '' THEN SET NEW.cContactAttachmentId = NewUlidString(); END IF;
END$$
DELIMITER ;
