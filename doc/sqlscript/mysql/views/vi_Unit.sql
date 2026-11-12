-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_Unit AS
SELECT u.*,
       (SELECT COUNT(*) FROM ta_TenantProduct p WHERE p.cUnitId = u.cUnitId) AS ccUnitProductCount
FROM ta_Unit u;
