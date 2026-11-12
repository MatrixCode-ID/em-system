-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantProduct AS
SELECT p.*, t.cTenantProductTypeCode AS cvTenantProductTypeCode, t.cTenantProductTypeName AS cvTenantProductTypeName,
       t.cTenantProductTypeKind AS cvTenantProductTypeKind, u.cUnitSymbol AS cvUnitSymbol,
       (SELECT COUNT(*) FROM ta_TenantProduct k WHERE k.cTenantProductParentId = p.cTenantProductId) AS ccTenantProductChildCount
FROM ta_TenantProduct p
JOIN ta_TenantProductType t ON t.cTenantProductTypeId = p.cTenantProductTypeId
JOIN ta_Unit u ON u.cUnitId = p.cUnitId;
