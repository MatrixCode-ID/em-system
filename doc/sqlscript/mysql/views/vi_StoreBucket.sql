-- MySQL 8.0+. Requires the corresponding application tables.
CREATE OR REPLACE VIEW `vi_StoreBucket` AS
SELECT b.`cStoreBucketId`, b.`cStoreBucketName`, b.`cStorePolicyId`, b.`cStoreBucketState`, b.`cStoreBucketNote`,
       b.`cStoreBucketBytes`, b.`cStoreBucketObjects`, b.`ustamp`, b.`datestamp`, b.`json_object`,
       CASE WHEN JSON_VALID(b.`json_object`) = 1 THEN JSON_VALUE(b.`json_object`, '$.owner' RETURNING CHAR(4000) NULL ON EMPTY NULL ON ERROR) END       AS `cvStoreBucketOwner`,
       CASE WHEN JSON_VALID(b.`json_object`) = 1 THEN JSON_VALUE(b.`json_object`, '$.description' RETURNING CHAR(4000) NULL ON EMPTY NULL ON ERROR) END AS `cvStoreBucketDescription`,
       p.`cStorePolicyName`, p.`cStorePolicyAccess`, p.`cStorePolicyReplicaMin`, p.`cStorePolicyReplicaTarget`,
       p.`cStorePolicyKeepVersions`, p.`cStorePolicyKeepDays`, p.`cStorePolicyQuotaBytes`,
       p.`cStorePolicyQuotaObjects`, p.`cStorePolicyMaxObjectBytes`
FROM `ta_StoreBucket` b
JOIN `ta_StorePolicy` p ON p.`cStorePolicyId` = b.`cStorePolicyId`;
