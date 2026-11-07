-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantPosition AS
SELECT p.*,
       (SELECT COUNT(*) FROM ta_TenantEmp e WHERE e.cTenantPositionId = p.cTenantPositionId AND e.cTenantEmpState = 0) AS ccTenantPositionEmpCount
FROM ta_TenantPosition p;
