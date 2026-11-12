/* Product type read model. Requires tables/080-tenant.sql and tables/120-product.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantProductType;
GO
CREATE VIEW dbo.vi_TenantProductType AS
SELECT t.*,
       (SELECT COUNT(*) FROM dbo.ta_TenantProduct p WHERE p.cTenantProductTypeId = t.cTenantProductTypeId) AS ccTenantProductTypeProductCount
FROM dbo.ta_TenantProductType t;
GO
SELECT TOP 100 * FROM dbo.vi_TenantProductType;
GO
