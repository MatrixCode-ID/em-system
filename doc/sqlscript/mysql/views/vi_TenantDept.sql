-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantDept AS
SELECT d.*, p.cTenantDeptName AS cvTenantDeptParentName, p.cTenantDeptCode AS cvTenantDeptParentCode,
       c.cContactFullName AS cvTenantDeptHeadName, h.cTenantEmpCode AS cvTenantDeptHeadCode,
       (SELECT COUNT(*) FROM ta_TenantDept k WHERE k.cTenantDeptParent_cTenantDeptId = d.cTenantDeptId) AS ccTenantDeptChildCount,
       (SELECT COUNT(*) FROM ta_TenantEmp e WHERE e.cTenantDeptId = d.cTenantDeptId AND e.cTenantEmpState = 0) AS ccTenantDeptEmpCount
FROM ta_TenantDept d
LEFT JOIN ta_TenantDept p ON p.cTenantDeptId = d.cTenantDeptParent_cTenantDeptId
LEFT JOIN ta_TenantEmp h ON h.cTenantEmpId = d.cTenantDeptHead_cTenantEmpId
LEFT JOIN ta_Contact c ON c.cContactId = h.cContactId;
