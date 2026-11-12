/* Products of a tenant, MySQL 8.0+. Requires the tenant tables and tables/100-business.sql (ta_Unit).
   An installation from before 2026-11-12 runs updates/20261112-Unit-Stamp.sql as well.
   Re-runnable; creates missing objects only. No seed data or physical deletion. */
CREATE TABLE IF NOT EXISTS ta_TenantProductType (
 cTenantProductTypeId char(26) NOT NULL, cTenantId char(26) NOT NULL, cTenantProductTypeCode varchar(50) NOT NULL,
 cTenantProductTypeName varchar(255) NOT NULL, cTenantProductTypeKind int NOT NULL,
 cTenantProductTypeState int NOT NULL DEFAULT 0, cTenantProductTypeOrder int NOT NULL DEFAULT -1, cTenantProductTypeNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantProductTypeId), UNIQUE KEY UQ_ta_TenantProductType_cTenantId_cTenantProductTypeCode(cTenantId,cTenantProductTypeCode),
 UNIQUE KEY UQ_ta_TenantProductType_cTenantProductTypeId_cTenantId(cTenantProductTypeId,cTenantId),
 CONSTRAINT FK_ta_TenantProductType_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE IF NOT EXISTS ta_TenantProduct (
 cTenantProductId char(26) NOT NULL, cTenantId char(26) NOT NULL, cTenantProductTypeId char(26) NOT NULL,
 cTenantProductParentId char(26) NULL, cUnitId varchar(50) NOT NULL, cTenantProductCode varchar(50) NOT NULL,
 cTenantProductName varchar(255) NOT NULL, cTenantProductState int NOT NULL DEFAULT 0, cTenantProductOrder int NOT NULL DEFAULT -1,
 cTenantProductNote varchar(500) NULL,
 ustamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), datestamp datetime(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), json_object longtext NULL,
 PRIMARY KEY(cTenantProductId), UNIQUE KEY UQ_ta_TenantProduct_cTenantId_cTenantProductCode(cTenantId,cTenantProductCode),
 UNIQUE KEY UQ_ta_TenantProduct_cTenantProductId_cTenantId(cTenantProductId,cTenantId),
 KEY IX_ta_TenantProduct_cTenantId_cTenantProductParentId_cTenantProductOrder(cTenantId,cTenantProductParentId,cTenantProductOrder),
 KEY IX_ta_TenantProduct_cTenantId_cTenantProductTypeId(cTenantId,cTenantProductTypeId),
 KEY IX_ta_TenantProduct_cUnitId(cUnitId),
 CONSTRAINT FK_ta_TenantProduct_ta_Tenant FOREIGN KEY(cTenantId) REFERENCES ta_Tenant(cTenantId),
 CONSTRAINT FK_ta_TenantProduct_ta_TenantProductType FOREIGN KEY(cTenantProductTypeId,cTenantId) REFERENCES ta_TenantProductType(cTenantProductTypeId,cTenantId),
 CONSTRAINT FK_ta_TenantProduct_ta_TenantProduct_Parent FOREIGN KEY(cTenantProductParentId,cTenantId) REFERENCES ta_TenantProduct(cTenantProductId,cTenantId),
 CONSTRAINT FK_ta_TenantProduct_ta_Unit FOREIGN KEY(cUnitId) REFERENCES ta_Unit(cUnitId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
DROP TRIGGER IF EXISTS tr_ta_TenantProductType_ulid;
DROP TRIGGER IF EXISTS tr_ta_TenantProduct_ulid;
DELIMITER $$
CREATE TRIGGER tr_ta_TenantProductType_ulid BEFORE INSERT ON ta_TenantProductType FOR EACH ROW
BEGIN
 IF NEW.cTenantProductTypeId IS NULL OR NEW.cTenantProductTypeId = '' THEN SET NEW.cTenantProductTypeId = NewUlidString(); END IF;
END$$
CREATE TRIGGER tr_ta_TenantProduct_ulid BEFORE INSERT ON ta_TenantProduct FOR EACH ROW
BEGIN
 IF NEW.cTenantProductId IS NULL OR NEW.cTenantProductId = '' THEN SET NEW.cTenantProductId = NewUlidString(); END IF;
END$$
DELIMITER ;
