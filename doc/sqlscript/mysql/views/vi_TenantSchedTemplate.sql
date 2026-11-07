-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW vi_TenantSchedTemplate AS
SELECT t.*,
       CAST(COALESCE((SELECT SUM(d.cTenantSchedTemplateDayHours) FROM ta_TenantSchedTemplateDay d WHERE d.cTenantSchedTemplateId = t.cTenantSchedTemplateId), 0) AS decimal(6,2)) AS ccTenantSchedTemplateWeekHours,
       (CASE WHEN EXISTS (SELECT 1 FROM ta_TenantSchedTemplateDay d WHERE d.cTenantSchedTemplateId = t.cTenantSchedTemplateId AND d.cTenantSchedTemplateDayHours > 0
          AND TIME_TO_SEC(t.cTenantSchedTemplateStart) / 60 + d.cTenantSchedTemplateDayHours * 60 + COALESCE(d.cTenantSchedTemplateDayBreak, t.cTenantSchedTemplateBreak) > 1440) THEN 1 ELSE 0 END) AS ccTenantSchedTemplateEndsNextDay
FROM ta_TenantSchedTemplate t;
