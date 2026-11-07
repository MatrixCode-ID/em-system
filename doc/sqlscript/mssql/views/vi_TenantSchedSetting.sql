/* Scheduling setting read model. Requires tables/010-core.sql, 080-tenant.sql, 090-tenant-org.sql, 110-attendance.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantSchedSetting;
GO
CREATE VIEW dbo.vi_TenantSchedSetting AS
SELECT s.*, d.cTenantDeptCode AS cvTenantDeptCode, d.cTenantDeptName AS cvTenantDeptName,
       t.cTenantSchedTemplateCode AS cvTenantSchedTemplateCode, t.cTenantSchedTemplateName AS cvTenantSchedTemplateName
FROM dbo.ta_TenantSchedSetting s
LEFT JOIN dbo.ta_TenantDept d ON d.cTenantDeptId = s.cTenantDeptId
LEFT JOIN dbo.ta_TenantSchedTemplate t ON t.cTenantSchedTemplateId = s.cTenantSchedTemplateId;
GO
SELECT TOP 100 * FROM dbo.vi_TenantSchedSetting;
GO
