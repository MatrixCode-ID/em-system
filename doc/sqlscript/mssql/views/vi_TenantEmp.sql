/* Tenant organization read model. Requires tables/010-core.sql, 080-tenant.sql, 090-tenant-org.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantEmp;
GO
CREATE VIEW dbo.vi_TenantEmp AS
SELECT e.*, c.cContactFullName AS cvContactFullName, c.cContactType AS cvContactType,
       d.cTenantDeptCode AS cvTenantDeptCode, d.cTenantDeptName AS cvTenantDeptName,
       CAST(CASE WHEN d.cTenantDeptHead_cTenantEmpId = e.cTenantEmpId THEN 1 ELSE 0 END AS bit) AS ccTenantEmpIsDeptHead,
       (SELECT STRING_AGG(CAST(u.cUserAccount AS varchar(max)), ', ') WITHIN GROUP (ORDER BY u.cUserAccount)
        FROM dbo.ta_User u WHERE u.cContactId = e.cContactId AND u.cUserState = 1) AS cvTenantEmpUserAccounts
FROM dbo.ta_TenantEmp e
JOIN dbo.ta_Contact c ON c.cContactId = e.cContactId
LEFT JOIN dbo.ta_TenantDept d ON d.cTenantDeptId = e.cTenantDeptId;
GO
SELECT TOP 100 * FROM dbo.vi_TenantEmp;
GO
