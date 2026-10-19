/*
    Tenant list of the business module manager, with the parent's name and code and the number of
    direct children (computed). Requires tables/080-tenant.sql.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DROP VIEW IF EXISTS [dbo].[vi_Tenant];
GO

CREATE VIEW [dbo].[vi_Tenant] AS
SELECT t.[cTenantId], t.[cTenantParent_cTenantId], t.[cTenantName], t.[cTenantCode], t.[cTenantState],
       t.[cTenantOrder], t.[cTenantNote], t.[ustamp], t.[datestamp], t.[json_object],
       p.[cTenantName] AS [cvTenantParentName],
       p.[cTenantCode] AS [cvTenantParentCode],
       (SELECT COUNT(*) FROM [dbo].[ta_Tenant] c WHERE c.[cTenantParent_cTenantId] = t.[cTenantId]) AS [ccTenantChildCount]
FROM [dbo].[ta_Tenant] t
LEFT JOIN [dbo].[ta_Tenant] p ON p.[cTenantId] = t.[cTenantParent_cTenantId];
GO

SELECT TOP 100 * FROM [dbo].[vi_Tenant] ORDER BY [cTenantOrder], [cTenantName];
GO
