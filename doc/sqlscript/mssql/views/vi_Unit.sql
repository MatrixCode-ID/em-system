/* Unit read model. Requires tables/100-business.sql and tables/120-product.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_Unit;
GO
CREATE VIEW dbo.vi_Unit AS
SELECT u.*,
       (SELECT COUNT(*) FROM dbo.ta_TenantProduct p WHERE p.cUnitId = u.cUnitId) AS ccUnitProductCount
FROM dbo.ta_Unit u;
GO
SELECT TOP 100 * FROM dbo.vi_Unit;
GO
