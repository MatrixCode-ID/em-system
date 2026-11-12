-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantProductType AS
SELECT t.*,
       (SELECT COUNT(*) FROM ta_TenantProduct p WHERE p.cTenantProductTypeId = t.cTenantProductTypeId) AS ccTenantProductTypeProductCount
FROM ta_TenantProductType t;
