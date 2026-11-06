-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantEmp AS
SELECT e.*, c.cContactFullName AS cvContactFullName, c.cContactType AS cvContactType,
       d.cTenantDeptCode AS cvTenantDeptCode, d.cTenantDeptName AS cvTenantDeptName,
       (CASE WHEN d.cTenantDeptHead_cTenantEmpId = e.cTenantEmpId THEN 1 ELSE 0 END) AS ccTenantEmpIsDeptHead,
       (SELECT GROUP_CONCAT(u.cUserAccount ORDER BY u.cUserAccount SEPARATOR ', ')
        FROM ta_User u WHERE u.cContactId = e.cContactId AND u.cUserState = 1) AS cvTenantEmpUserAccounts
FROM ta_TenantEmp e
JOIN ta_Contact c ON c.cContactId = e.cContactId
LEFT JOIN ta_TenantDept d ON d.cTenantDeptId = e.cTenantDeptId;
