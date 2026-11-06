-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_StoreVolume` AS
SELECT v.`cStoreVolumeId`, v.`cStoreVolumeName`, v.`cStoreVolumeState`, v.`cStoreVolumeType`,
       v.`cStoreVolumePath`, v.`cStoreVolumeNote`, v.`ustamp`, v.`datestamp`, v.`json_object`,
       IFNULL(c.`cvStoreVolumeBlobs`, 0)   AS `cvStoreVolumeBlobs`,
       IFNULL(c.`cvStoreVolumeBytes`, 0)   AS `cvStoreVolumeBytes`,
       IFNULL(c.`cvStoreVolumeMissing`, 0) AS `cvStoreVolumeMissing`
FROM `ta_StoreVolume` v
LEFT JOIN (
    SELECT bv.`cStoreVolumeId`,
           SUM(CASE WHEN bv.`cStoreObjectBlobVolState` = 2 THEN 1 ELSE 0 END) AS `cvStoreVolumeBlobs`,
           SUM(CASE WHEN bv.`cStoreObjectBlobVolState` = 2 THEN b.`cStoreObjectBlobSize` ELSE 0 END) AS `cvStoreVolumeBytes`,
           SUM(CASE WHEN bv.`cStoreObjectBlobVolState` = 3 THEN 1 ELSE 0 END) AS `cvStoreVolumeMissing`
    FROM `ta_StoreObjectBlobVol` bv
    JOIN `ta_StoreObjectBlob` b ON b.`cStoreObjectBlobHash` = bv.`cStoreObjectBlobHash`
    GROUP BY bv.`cStoreVolumeId`
) c ON c.`cStoreVolumeId` = v.`cStoreVolumeId`;
