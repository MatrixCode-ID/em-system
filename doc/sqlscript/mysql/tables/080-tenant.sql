/* Tenant hierarchy, MySQL 8.0+. Requires sets/000-ulid.sql. No rows are seeded. */
CREATE TABLE IF NOT EXISTS ta_Tenant (
 cTenantId char(26) NOT NULL,
 cTenantParent_cTenantId char(26) NULL,
 cTenantName varchar(255) NOT NULL,
 cTenantCode varchar(100) NOT NULL,
 cTenantState int NOT NULL DEFAULT 0,
 cTenantOrder int NOT NULL DEFAULT -1,
 cTenantNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
 datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
 json_object longtext NULL,
 PRIMARY KEY(cTenantId), UNIQUE KEY UQ_ta_Tenant_cTenantCode(cTenantCode),
 KEY IX_ta_Tenant_cTenantParent_cTenantId(cTenantParent_cTenantId),
 CONSTRAINT FK_ta_Tenant_ta_Tenant_Parent FOREIGN KEY(cTenantParent_cTenantId) REFERENCES ta_Tenant(cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
DROP TRIGGER IF EXISTS tr_ta_Tenant_ulid;
DELIMITER $$
CREATE TRIGGER tr_ta_Tenant_ulid BEFORE INSERT ON ta_Tenant FOR EACH ROW
BEGIN
 IF NEW.cTenantId IS NULL OR NEW.cTenantId = '' THEN SET NEW.cTenantId = NewUlidString(); END IF;
END$$
DELIMITER ;
