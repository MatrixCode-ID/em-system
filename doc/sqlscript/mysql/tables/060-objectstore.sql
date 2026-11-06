/* MySQL 8.0+ application schema. Install after its core dependencies. */

CREATE TABLE IF NOT EXISTS `ta_StoreVolume`
    (
        `cStoreVolumeId`    char(26)       NOT NULL,
        `cStoreVolumeName`  varchar(100)   COLLATE utf8mb4_0900_ai_ci NOT NULL,
        `cStoreVolumeState` int            NOT NULL,
        `cStoreVolumeType`  int            NOT NULL,
        `cStoreVolumePath`  varchar(400)  NOT NULL,
        `cStoreVolumeNote`  varchar(500)  NULL,
        `ustamp`            datetime(3)       NOT NULL,
        `datestamp`         datetime(3)       NOT NULL,
        `json_object`       longtext  NULL,
        CONSTRAINT `PK_StoreVolume` PRIMARY KEY (`cStoreVolumeId`),
        CONSTRAINT `UQ_StoreVolumeName` UNIQUE (`cStoreVolumeName`),
        CONSTRAINT `CK_StoreVolumeState` CHECK (`cStoreVolumeState` IN (1, 2, 3)),
        CONSTRAINT `CK_StoreVolumeType` CHECK (`cStoreVolumeType` IN (1))
    
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_StorePolicy`
    (
        `cStorePolicyId`             char(26)       NOT NULL,
        `cStorePolicyName`           varchar(100)   COLLATE utf8mb4_0900_ai_ci NOT NULL,
        `cStorePolicyState`          int            NOT NULL,
        `cStorePolicyNote`           varchar(500)  NULL,
        `cStorePolicyReplicaMin`     int            NOT NULL DEFAULT 1,
        `cStorePolicyReplicaTarget`  int            NOT NULL DEFAULT 1,
        `cStorePolicyKeepVersions`   int            NULL,
        `cStorePolicyKeepDays`       int            NULL,
        `cStorePolicyQuotaBytes`     bigint         NULL,
        `cStorePolicyQuotaObjects`   bigint         NULL,
        `cStorePolicyMaxObjectBytes` bigint         NULL,
        `cStorePolicyAccess`         int            NOT NULL DEFAULT 1,
        `ustamp`                     datetime(3)       NOT NULL,
        `datestamp`                  datetime(3)       NOT NULL,
        `json_object`                longtext  NULL,
        CONSTRAINT `PK_StorePolicy` PRIMARY KEY (`cStorePolicyId`),
        CONSTRAINT `UQ_StorePolicyName` UNIQUE (`cStorePolicyName`),
        CONSTRAINT `CK_StorePolicyState` CHECK (`cStorePolicyState` IN (1, 2)),
        CONSTRAINT `CK_StorePolicyReplica` CHECK (`cStorePolicyReplicaMin` >= 1 AND `cStorePolicyReplicaMin` <= `cStorePolicyReplicaTarget`),
        CONSTRAINT `CK_StorePolicyKeepVersions` CHECK (`cStorePolicyKeepVersions` IS NULL OR `cStorePolicyKeepVersions` >= 0),
        CONSTRAINT `CK_StorePolicyKeepDays` CHECK (`cStorePolicyKeepDays` IS NULL OR `cStorePolicyKeepDays` >= 1),
        CONSTRAINT `CK_StorePolicyQuotaBytes` CHECK (`cStorePolicyQuotaBytes` IS NULL OR `cStorePolicyQuotaBytes` >= 0),
        CONSTRAINT `CK_StorePolicyQuotaObjects` CHECK (`cStorePolicyQuotaObjects` IS NULL OR `cStorePolicyQuotaObjects` >= 0),
        CONSTRAINT `CK_StorePolicyMaxObjectBytes` CHECK (`cStorePolicyMaxObjectBytes` IS NULL OR `cStorePolicyMaxObjectBytes` >= 1),
        CONSTRAINT `CK_StorePolicyAccess` CHECK (`cStorePolicyAccess` IN (1, 2))
    
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_StorePolicyVolume`
    (
        `cStorePolicyId`          char(26) NOT NULL,
        `cStoreVolumeId`          char(26) NOT NULL,
        `cStorePolicyVolumeOrder` int      NOT NULL,
        CONSTRAINT `PK_StorePolicyVolume` PRIMARY KEY (`cStorePolicyId`, `cStoreVolumeId`),
        CONSTRAINT `FK_StorePolicyVolumePolicy` FOREIGN KEY (`cStorePolicyId`) REFERENCES `ta_StorePolicy` (`cStorePolicyId`) ON DELETE CASCADE,
        CONSTRAINT `FK_StorePolicyVolumeVolume` FOREIGN KEY (`cStoreVolumeId`) REFERENCES `ta_StoreVolume` (`cStoreVolumeId`)
    ,
   KEY `IX_StorePolicyVolumeVolume` (`cStoreVolumeId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_StoreBucket`
    (
        `cStoreBucketId`      char(26)       NOT NULL,
        `cStoreBucketName`    varchar(63)    COLLATE utf8mb4_bin NOT NULL,
        `cStorePolicyId`      char(26)       NOT NULL,
        `cStoreBucketState`   int            NOT NULL,
        `cStoreBucketNote`    varchar(500)  NULL,
        `cStoreBucketBytes`   bigint         NOT NULL DEFAULT 0,
        `cStoreBucketObjects` bigint         NOT NULL DEFAULT 0,
        `ustamp`              datetime(3)       NOT NULL,
        `datestamp`           datetime(3)       NOT NULL,
        `json_object`         longtext  NULL,
        CONSTRAINT `PK_StoreBucket` PRIMARY KEY (`cStoreBucketId`),
        CONSTRAINT `UQ_StoreBucketName` UNIQUE (`cStoreBucketName`),
        CONSTRAINT `FK_StoreBucketPolicy` FOREIGN KEY (`cStorePolicyId`) REFERENCES `ta_StorePolicy` (`cStorePolicyId`),
        CONSTRAINT `CK_StoreBucketState` CHECK (`cStoreBucketState` IN (1, 2)),
        CONSTRAINT `CK_StoreBucketCounters` CHECK (`cStoreBucketBytes` >= 0 AND `cStoreBucketObjects` >= 0)
    ,
   KEY `IX_StoreBucketPolicy` (`cStorePolicyId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_StoreObject`
    (
        `cStoreObjectId`             char(26)        NOT NULL,
        `cStoreBucketId`             char(26)        NOT NULL,
        `cStoreObjectKey`            varchar(1024)  COLLATE utf8mb4_bin NOT NULL,
        `cStoreObjectKeyHash`        binary(32)      NOT NULL,
        -- First 400 characters of the key, indexed for prefix listing (see the header).
        `cStoreObjectKeyPrefix`      varchar(400) COLLATE utf8mb4_bin GENERATED ALWAYS AS (LEFT(`cStoreObjectKey`, 400)) STORED,
        `cStoreObjectVersionCurrent` int             NULL,
        `cStoreObjectVersionNext`    int             NOT NULL DEFAULT 1,
        `cStoreObjectState`          int             NOT NULL,
        `ustamp`                     datetime(3)        NOT NULL,
        `datestamp`                  datetime(3)        NOT NULL,
        `json_object`                longtext   NULL,
        CONSTRAINT `PK_StoreObject` PRIMARY KEY (`cStoreObjectId`),
        CONSTRAINT `UQ_StoreObjectKey` UNIQUE (`cStoreBucketId`, `cStoreObjectKeyHash`),
        CONSTRAINT `FK_StoreObjectBucket` FOREIGN KEY (`cStoreBucketId`) REFERENCES `ta_StoreBucket` (`cStoreBucketId`),
        CONSTRAINT `CK_StoreObjectVersionNext` CHECK (`cStoreObjectVersionNext` >= 1)
    ,
   KEY `IX_StoreObjectKeyPrefix` (`cStoreBucketId`, `cStoreObjectKeyPrefix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_StoreObjectBlob`
    (
        `cStoreObjectBlobHash`     binary(32) NOT NULL,
        `cStoreObjectBlobCrc32`    binary(4)  NOT NULL,
        `cStoreObjectBlobSize`     bigint     NOT NULL,
        `cStoreObjectBlobState`    int        NOT NULL,
        `cStoreObjectBlobOrphanAt` datetime(3)   NULL,
        `datestamp`                datetime(3)   NOT NULL,
        CONSTRAINT `PK_StoreObjectBlob` PRIMARY KEY (`cStoreObjectBlobHash`),
        CONSTRAINT `CK_StoreObjectBlobState` CHECK (`cStoreObjectBlobState` IN (1, 2)),
        CONSTRAINT `CK_StoreObjectBlobSize` CHECK (`cStoreObjectBlobSize` >= 0)
    ,
   KEY `IX_StoreObjectBlobState` (`cStoreObjectBlobState`, `cStoreObjectBlobOrphanAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_StoreObjectVersion`
    (
        `cStoreObjectId`            char(26)       NOT NULL,
        `cStoreObjectVersionId`     int            NOT NULL,
        `cStoreObjectBlobHash`      binary(32)     NULL,
        `cStoreObjectVersionSize`   bigint         NOT NULL,
        `cStoreObjectVersionMime`   varchar(255)   NOT NULL,
        `cStoreObjectVersionMarker` bit            NOT NULL,
        `datestamp`                 datetime(3)       NOT NULL,
        `json_object`               longtext  NULL,
        CONSTRAINT `PK_StoreObjectVersion` PRIMARY KEY (`cStoreObjectId`, `cStoreObjectVersionId`),
        CONSTRAINT `FK_StoreObjectVersionObject` FOREIGN KEY (`cStoreObjectId`) REFERENCES `ta_StoreObject` (`cStoreObjectId`),
        CONSTRAINT `FK_StoreObjectVersionBlob` FOREIGN KEY (`cStoreObjectBlobHash`) REFERENCES `ta_StoreObjectBlob` (`cStoreObjectBlobHash`),
        -- A delete marker has no blob and no size; any other version has a blob.
        CONSTRAINT `CK_StoreObjectVersionMarker` CHECK (
            (`cStoreObjectVersionMarker` = 1 AND `cStoreObjectBlobHash` IS NULL AND `cStoreObjectVersionSize` = 0)
         OR (`cStoreObjectVersionMarker` = 0 AND `cStoreObjectBlobHash` IS NOT NULL AND `cStoreObjectVersionSize` >= 0))
    ,
   KEY `IX_StoreObjectVersionBlob` (`cStoreObjectBlobHash`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_StoreObjectBlobVol`
    (
        `cStoreObjectBlobHash`          binary(32) NOT NULL,
        `cStoreVolumeId`                char(26)   NOT NULL,
        `cStoreObjectBlobVolState`      int        NOT NULL,
        `cStoreObjectBlobVolVerifiedAt` datetime(3)   NULL,
        `datestamp`                     datetime(3)   NOT NULL,
        CONSTRAINT `PK_StoreObjectBlobVol` PRIMARY KEY (`cStoreObjectBlobHash`, `cStoreVolumeId`),
        CONSTRAINT `FK_StoreObjectBlobVolBlob` FOREIGN KEY (`cStoreObjectBlobHash`) REFERENCES `ta_StoreObjectBlob` (`cStoreObjectBlobHash`),
        CONSTRAINT `FK_StoreObjectBlobVolVolume` FOREIGN KEY (`cStoreVolumeId`) REFERENCES `ta_StoreVolume` (`cStoreVolumeId`),
        CONSTRAINT `CK_StoreObjectBlobVolState` CHECK (`cStoreObjectBlobVolState` IN (1, 2, 3, 4))
    ,
   KEY `IX_StoreObjectBlobVolVolume` (`cStoreVolumeId`, `cStoreObjectBlobVolState`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT IGNORE INTO ta_Meta (cMetaKey,cMetaValue,cMetaDescription,ustamp) VALUES ('ObjectStoreSchemaVersion','1','Object storage schema version',UTC_TIMESTAMP(3));
