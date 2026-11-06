-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_StoreObject` AS
SELECT o.`cStoreObjectId`, o.`cStoreBucketId`, o.`cStoreObjectKey`, o.`cStoreObjectKeyHash`,
       o.`cStoreObjectVersionCurrent`, o.`cStoreObjectVersionNext`, o.`cStoreObjectState`,
       o.`ustamp`, o.`datestamp`, o.`json_object`,
       v.`cStoreObjectBlobHash`, v.`cStoreObjectVersionSize`, v.`cStoreObjectVersionMime`,
       v.`cStoreObjectVersionMarker`, v.`datestamp` AS `cvStoreObjectVersionDate`
FROM `ta_StoreObject` o
LEFT JOIN `ta_StoreObjectVersion` v
       ON v.`cStoreObjectId` = o.`cStoreObjectId` AND v.`cStoreObjectVersionId` = o.`cStoreObjectVersionCurrent`;
