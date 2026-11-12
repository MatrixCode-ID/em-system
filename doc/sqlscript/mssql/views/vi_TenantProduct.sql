/* Product read model. Requires tables/080-tenant.sql, tables/100-business.sql and tables/120-product.sql. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
DROP VIEW IF EXISTS dbo.vi_TenantProduct;
GO
CREATE VIEW dbo.vi_TenantProduct AS
SELECT p.*, t.cTenantProductTypeCode AS cvTenantProductTypeCode, t.cTenantProductTypeName AS cvTenantProductTypeName,
       t.cTenantProductTypeKind AS cvTenantProductTypeKind, u.cUnitSymbol AS cvUnitSymbol,
       (SELECT COUNT(*) FROM dbo.ta_TenantProduct k WHERE k.cTenantProductParentId = p.cTenantProductId) AS ccTenantProductChildCount
FROM dbo.ta_TenantProduct p
JOIN dbo.ta_TenantProductType t ON t.cTenantProductTypeId = p.cTenantProductTypeId
JOIN dbo.ta_Unit u ON u.cUnitId = p.cUnitId;
GO
SELECT TOP 100 * FROM dbo.vi_TenantProduct;
GO
