/* Tenant position read model. Requires tables/010-core.sql, 080-tenant.sql, 090-tenant-org.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantPosition;
GO
CREATE VIEW dbo.vi_TenantPosition AS
SELECT p.*,
       (SELECT COUNT(*) FROM dbo.ta_TenantEmp e WHERE e.cTenantPositionId = p.cTenantPositionId AND e.cTenantEmpState = 0) AS ccTenantPositionEmpCount
FROM dbo.ta_TenantPosition p;
GO
SELECT TOP 100 * FROM dbo.vi_TenantPosition;
GO
