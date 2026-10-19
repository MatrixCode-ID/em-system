/* Tenant organization read model. Requires tables/010-core.sql, 080-tenant.sql, 090-tenant-org.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantDept;
GO
CREATE VIEW dbo.vi_TenantDept AS
SELECT d.*, p.cTenantDeptName AS cvTenantDeptParentName, p.cTenantDeptCode AS cvTenantDeptParentCode,
       c.cContactFullName AS cvTenantDeptHeadName, h.cTenantEmpCode AS cvTenantDeptHeadCode,
       (SELECT COUNT(*) FROM dbo.ta_TenantDept k WHERE k.cTenantDeptParent_cTenantDeptId = d.cTenantDeptId) AS ccTenantDeptChildCount,
       (SELECT COUNT(*) FROM dbo.ta_TenantEmp e WHERE e.cTenantDeptId = d.cTenantDeptId AND e.cTenantEmpState = 0) AS ccTenantDeptEmpCount
FROM dbo.ta_TenantDept d
LEFT JOIN dbo.ta_TenantDept p ON p.cTenantDeptId = d.cTenantDeptParent_cTenantDeptId
LEFT JOIN dbo.ta_TenantEmp h ON h.cTenantEmpId = d.cTenantDeptHead_cTenantEmpId
LEFT JOIN dbo.ta_Contact c ON c.cContactId = h.cContactId;
GO
SELECT TOP 100 * FROM dbo.vi_TenantDept;
GO
