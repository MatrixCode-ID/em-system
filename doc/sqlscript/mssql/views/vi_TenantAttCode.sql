/* Attendance code read model. Requires tables/010-core.sql, 080-tenant.sql, 110-attendance.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantAttCode;
GO
CREATE VIEW dbo.vi_TenantAttCode AS
SELECT c.* FROM dbo.ta_TenantAttCode c;
GO
SELECT TOP 100 * FROM dbo.vi_TenantAttCode;
GO
