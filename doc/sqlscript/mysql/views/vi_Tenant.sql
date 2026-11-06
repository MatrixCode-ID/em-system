-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_Tenant` AS
SELECT t.`cTenantId`, t.`cTenantParent_cTenantId`, t.`cTenantName`, t.`cTenantCode`, t.`cTenantState`,
       t.`cTenantOrder`, t.`cTenantNote`, t.`ustamp`, t.`datestamp`, t.`json_object`,
       p.`cTenantName` AS `cvTenantParentName`,
       p.`cTenantCode` AS `cvTenantParentCode`,
       (SELECT COUNT(*) FROM `ta_Tenant` c WHERE c.`cTenantParent_cTenantId` = t.`cTenantId`) AS `ccTenantChildCount`
FROM `ta_Tenant` t
LEFT JOIN `ta_Tenant` p ON p.`cTenantId` = t.`cTenantParent_cTenantId`;
