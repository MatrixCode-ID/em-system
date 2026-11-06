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
CREATE TABLE IF NOT EXISTS ta_TenantEmp (
 cTenantEmpId char(26) NOT NULL, cTenantId char(26) NOT NULL, cContactId char(26) NOT NULL,
 cTenantDeptId char(26) NULL, cTenantEmpCode varchar(50) NOT NULL, cTenantEmpPosition varchar(255) NULL,
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
DROP TRIGGER IF EXISTS tr_ta_TenantDept_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantEmp_ulid;
DELIMITER $$
CREATE TRIGGER tr_ta_TenantDept_ulid BEFORE INSERT ON ta_TenantDept FOR EACH ROW
BEGIN
 IF NEW.cTenantDeptId IS NULL OR NEW.cTenantDeptId = '' THEN SET NEW.cTenantDeptId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantEmp_ulid BEFORE INSERT ON ta_TenantEmp FOR EACH ROW
BEGIN
 IF NEW.cTenantEmpId IS NULL OR NEW.cTenantEmpId = '' THEN SET NEW.cTenantEmpId = NewUlidString(); END IF;
END$$
DELIMITER ;
