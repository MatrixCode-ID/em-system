/* Schedule template read model. Requires tables/010-core.sql, 080-tenant.sql, 090-tenant-org.sql, 110-attendance.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantSchedTemplate;
GO
CREATE VIEW dbo.vi_TenantSchedTemplate AS
SELECT t.*,
       CAST(COALESCE((SELECT SUM(d.cTenantSchedTemplateDayHours) FROM dbo.ta_TenantSchedTemplateDay d WHERE d.cTenantSchedTemplateId = t.cTenantSchedTemplateId), 0) AS decimal(6,2)) AS ccTenantSchedTemplateWeekHours,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ta_TenantSchedTemplateDay d WHERE d.cTenantSchedTemplateId = t.cTenantSchedTemplateId AND d.cTenantSchedTemplateDayHours > 0
          AND DATEPART(HOUR, t.cTenantSchedTemplateStart) * 60 + DATEPART(MINUTE, t.cTenantSchedTemplateStart) + d.cTenantSchedTemplateDayHours * 60 + COALESCE(d.cTenantSchedTemplateDayBreak, t.cTenantSchedTemplateBreak) > 1440) THEN 1 ELSE 0 END AS bit) AS ccTenantSchedTemplateEndsNextDay
FROM dbo.ta_TenantSchedTemplate t;
GO
SELECT TOP 100 * FROM dbo.vi_TenantSchedTemplate;
GO
