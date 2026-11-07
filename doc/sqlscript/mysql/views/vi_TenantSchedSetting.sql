-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantSchedSetting AS
SELECT s.*, d.cTenantDeptCode AS cvTenantDeptCode, d.cTenantDeptName AS cvTenantDeptName,
       t.cTenantSchedTemplateCode AS cvTenantSchedTemplateCode, t.cTenantSchedTemplateName AS cvTenantSchedTemplateName
FROM ta_TenantSchedSetting s
LEFT JOIN ta_TenantDept d ON d.cTenantDeptId = s.cTenantDeptId
LEFT JOIN ta_TenantSchedTemplate t ON t.cTenantSchedTemplateId = s.cTenantSchedTemplateId;
