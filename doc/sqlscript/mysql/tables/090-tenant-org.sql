/* Tenant organization, MySQL 8.0+. Requires core and tenant tables. */
CREATE TABLE IF NOT EXISTS ta_TenantDept (
 cTenantDeptId char(26) NOT NULL, cTenantId char(26) NOT NULL,
 cTenantDeptParent_cTenantDeptId char(26) NULL, cTenantDeptCode varchar(50) NOT NULL,
 cTenantDeptName varchar(255) NOT NULL, cTenantDeptAbv varchar(10) NULL,
 cTenantDeptState int NOT NULL DEFAULT 0, cTenantDeptOrder int NOT NULL DEFAULT -1,
 cTenantDeptNote varchar(500) NULL, cTenantDeptHead_cTenantEmpId char(26) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantDeptId), UNIQUE KEY UQ_ta_TenantDept_cTenantId_cTenantDeptCode(cTenantId,cTenantDeptCode),
 UNIQUE KEY UQ_ta_TenantDept_cTenantDeptId_cTenantId(cTenantDeptId,cTenantId),
 KEY IX_ta_TenantDept_cTenantDeptParent_cTenantDeptId(cTenantDeptParent_cTenantDeptId),
 CONSTRAINT FK_ta_TenantDept_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId),
 CONSTRAINT FK_ta_TenantDept_ta_TenantDept_Parent FOREIGN KEY(cTenantDeptParent_cTenantDeptId,cTenantId) REFERENCES ta_TenantDept(cTenantDeptId,cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_TenantPosition (
 cTenantPositionId char(26) NOT NULL, cTenantId char(26) NOT NULL, cTenantPositionCode varchar(50) NOT NULL,
 cTenantPositionName varchar(255) NOT NULL, cTenantPositionState int NOT NULL DEFAULT 0, cTenantPositionNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantPositionId), UNIQUE KEY UQ_ta_TenantPosition_cTenantId_cTenantPositionCode(cTenantId,cTenantPositionCode),
 UNIQUE KEY UQ_ta_TenantPosition_cTenantPositionId_cTenantId(cTenantPositionId,cTenantId),
 CONSTRAINT FK_ta_TenantPosition_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_TenantEmp (
 cTenantEmpId char(26) NOT NULL, cTenantId char(26) NOT NULL, cContactId char(26) NOT NULL,
 cTenantDeptId char(26) NULL, cTenantEmpCode varchar(50) NOT NULL, cTenantPositionId char(26) NULL,
 cTenantEmpState int NOT NULL DEFAULT 0, cTenantEmpNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantEmpId), UNIQUE KEY UQ_ta_TenantEmp_cTenantId_cTenantEmpCode(cTenantId,cTenantEmpCode),
 UNIQUE KEY UQ_ta_TenantEmp_cTenantId_cContactId(cTenantId,cContactId),
 UNIQUE KEY UQ_ta_TenantEmp_cTenantEmpId_cTenantDeptId(cTenantEmpId,cTenantDeptId),
 KEY IX_ta_TenantEmp_cContactId(cContactId), KEY IX_ta_TenantEmp_cTenantDeptId(cTenantDeptId),
 CONSTRAINT FK_ta_TenantEmp_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId),
 CONSTRAINT FK_ta_TenantEmp_ta_Contact FOREIGN KEY(cContactId) REFERENCES ta_Contact(cContactId),
 CONSTRAINT FK_ta_TenantEmp_ta_TenantDept FOREIGN KEY(cTenantDeptId,cTenantId) REFERENCES ta_TenantDept(cTenantDeptId,cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_ContactAttachment (
 cContactAttachmentId char(26) NOT NULL, cContactId char(26) NOT NULL, cStoreObjectId char(26) NOT NULL,
 cContactAttachmentName varchar(255) NOT NULL, cContactAttachmentKind int NOT NULL DEFAULT 99, cContactAttachmentNote varchar(500) NULL,
 cContactAttachmentState int NOT NULL DEFAULT 0, cContactAttachmentOrder int NOT NULL DEFAULT 0,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cContactAttachmentId), KEY IX_ta_ContactAttachment_cContactId(cContactId),
 CONSTRAINT FK_ta_ContactAttachment_ta_Contact FOREIGN KEY(cContactId) REFERENCES ta_Contact(cContactId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
DROP PROCEDURE IF EXISTS mysql_add_tenant_head_fk;
DELIMITER $$
CREATE PROCEDURE mysql_add_tenant_head_fk()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='FK_ta_TenantDept_ta_TenantEmp_Head') THEN
  ALTER TABLE ta_TenantDept ADD CONSTRAINT FK_ta_TenantDept_ta_TenantEmp_Head FOREIGN KEY(cTenantDeptHead_cTenantEmpId,cTenantDeptId) REFERENCES ta_TenantEmp(cTenantEmpId,cTenantDeptId);
 END IF;
END$$
DELIMITER ;
CALL mysql_add_tenant_head_fk();
DROP PROCEDURE mysql_add_tenant_head_fk;
DROP PROCEDURE IF EXISTS mysql_add_tenant_position_link;
DELIMITER $$
CREATE PROCEDURE mysql_add_tenant_position_link()
BEGIN
 -- An older installation keeps its table; updates/20261107-TenantPosition.sql then moves and removes the text column.
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_TenantEmp' AND COLUMN_NAME='cTenantPositionId') THEN
  ALTER TABLE ta_TenantEmp ADD cTenantPositionId char(26) NULL;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='FK_ta_TenantEmp_ta_TenantPosition') THEN
  ALTER TABLE ta_TenantEmp ADD CONSTRAINT FK_ta_TenantEmp_ta_TenantPosition FOREIGN KEY(cTenantPositionId,cTenantId) REFERENCES ta_TenantPosition(cTenantPositionId,cTenantId);
 END IF;
END$$
DELIMITER ;
CALL mysql_add_tenant_position_link();
DROP PROCEDURE mysql_add_tenant_position_link;
DROP TRIGGER IF EXISTS tr_ta_TenantDept_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantEmp_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantPosition_ulid;
DROP TRIGGER IF EXISTS tr_ta_ContactAttachment_ulid;
DELIMITER $$
CREATE TRIGGER tr_ta_TenantDept_ulid BEFORE INSERT ON ta_TenantDept FOR EACH ROW
BEGIN
 IF NEW.cTenantDeptId IS NULL OR NEW.cTenantDeptId = '' THEN SET NEW.cTenantDeptId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantEmp_ulid BEFORE INSERT ON ta_TenantEmp FOR EACH ROW
BEGIN
 IF NEW.cTenantEmpId IS NULL OR NEW.cTenantEmpId = '' THEN SET NEW.cTenantEmpId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantPosition_ulid BEFORE INSERT ON ta_TenantPosition FOR EACH ROW
BEGIN
 IF NEW.cTenantPositionId IS NULL OR NEW.cTenantPositionId = '' THEN SET NEW.cTenantPositionId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_ContactAttachment_ulid BEFORE INSERT ON ta_ContactAttachment FOR EACH ROW
BEGIN
 IF NEW.cContactAttachmentId IS NULL OR NEW.cContactAttachmentId = '' THEN SET NEW.cContactAttachmentId = NewUlidString(); END IF;
END$$
DELIMITER ;
