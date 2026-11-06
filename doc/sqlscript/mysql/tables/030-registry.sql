/* MySQL 8.0+ application schema. Install after its core dependencies. */

CREATE TABLE IF NOT EXISTS `ta_CtnRoot`
      (
         `cCtnRootId`          char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnRootName`        varchar(64)  COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnRootState`       int          NOT NULL,
         `cCtnRootDescription` varchar(500) COLLATE utf8mb4_0900_ai_ci NULL,
         `ustamp`              datetime(3)     NOT NULL,
         `datestamp`           datetime(3)     NOT NULL,
         `json_object`         longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_CtnRoot` PRIMARY KEY  (`cCtnRootId`),
         CONSTRAINT `UQ_ta_CtnRoot_cCtnRootName` UNIQUE  (`cCtnRootName`)
      
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnFolder`
      (
         `cCtnFolderId`                  char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnRootId`                    char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         -- Empty = directly in the root.
         `cCtnFolderParent_cCtnFolderId` char(26)     COLLATE utf8mb4_0900_ai_ci NULL,
         `cCtnFolderName`                varchar(100) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnFolderOrder`               int          NOT NULL DEFAULT -1,
         `ustamp`                        datetime(3)     NOT NULL,
         `datestamp`                     datetime(3)     NOT NULL,
         `json_object`                   longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_CtnFolder` PRIMARY KEY  (`cCtnFolderId`),
         CONSTRAINT `FK_ta_CtnFolder_ta_CtnRoot` FOREIGN KEY (`cCtnRootId`) REFERENCES `ta_CtnRoot` (`cCtnRootId`),
         CONSTRAINT `FK_ta_CtnFolder_cCtnFolderParent_cCtnFolderId` FOREIGN KEY (`cCtnFolderParent_cCtnFolderId`) REFERENCES `ta_CtnFolder` (`cCtnFolderId`)
      ,
   KEY `IX_ta_CtnFolder_cCtnRootId` (`cCtnRootId`),
   KEY `IX_ta_CtnFolder_Parent` (`cCtnFolderParent_cCtnFolderId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnImage`
      (
         `cCtnImageId`          char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnRootId`           char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnFolderId`         char(26)     COLLATE utf8mb4_0900_ai_ci NULL,
         `cCtnImageName`        varchar(128) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnImageState`       int          NOT NULL,
         `cCtnImageDescription` varchar(500) COLLATE utf8mb4_0900_ai_ci NULL,
         `ustamp`               datetime(3)     NOT NULL,
         `datestamp`            datetime(3)     NOT NULL,
         `json_object`          longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_CtnImage` PRIMARY KEY  (`cCtnImageId`),
         CONSTRAINT `UQ_ta_CtnImage_Root_Name` UNIQUE  (`cCtnRootId`, `cCtnImageName`),
         CONSTRAINT `FK_ta_CtnImage_ta_CtnRoot` FOREIGN KEY (`cCtnRootId`) REFERENCES `ta_CtnRoot` (`cCtnRootId`),
         CONSTRAINT `FK_ta_CtnImage_ta_CtnFolder` FOREIGN KEY (`cCtnFolderId`) REFERENCES `ta_CtnFolder` (`cCtnFolderId`)
      ,
   KEY `IX_ta_CtnImage_cCtnFolderId` (`cCtnFolderId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnManifest`
      (
         `cCtnManifestId`                  char(26)      COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnImageId`                     char(26)      COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnManifestDigest`              varchar(128)  COLLATE utf8mb4_0900_as_cs NOT NULL,
         `cCtnManifestMediaType`           varchar(255)  COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnManifestSize`                bigint        NOT NULL,
         -- The bytes exactly as sent: the digest is computed from its bytes.
         `cCtnManifestContent`             longblob NOT NULL,
         `cCtnManifestPushedBy_cRobotId` char(26)     COLLATE utf8mb4_0900_ai_ci NULL,
         `ustamp`                          datetime(3)      NOT NULL,
         `datestamp`                       datetime(3)      NOT NULL,
         `json_object`                     longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_CtnManifest` PRIMARY KEY  (`cCtnManifestId`),
         CONSTRAINT `UQ_ta_CtnManifest_Image_Digest` UNIQUE  (`cCtnImageId`, `cCtnManifestDigest`),
         CONSTRAINT `FK_ta_CtnManifest_ta_CtnImage` FOREIGN KEY (`cCtnImageId`) REFERENCES `ta_CtnImage` (`cCtnImageId`) ON DELETE CASCADE,
         CONSTRAINT `FK_ta_CtnManifest_cCtnManifestPushedBy_cRobotId` FOREIGN KEY (`cCtnManifestPushedBy_cRobotId`) REFERENCES `ta_Robot` (`cRobotId`)
      
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnTag`
      (
         `cCtnImageId`    char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnTagName`    varchar(128) COLLATE utf8mb4_0900_as_cs NOT NULL,
         `cCtnManifestId` char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `ustamp`         datetime(3)     NOT NULL,
         `datestamp`      datetime(3)     NOT NULL,
         CONSTRAINT `PK_ta_CtnTag` PRIMARY KEY  (`cCtnImageId`, `cCtnTagName`),
         CONSTRAINT `FK_ta_CtnTag_ta_CtnImage` FOREIGN KEY (`cCtnImageId`) REFERENCES `ta_CtnImage` (`cCtnImageId`) ON DELETE CASCADE,
         CONSTRAINT `FK_ta_CtnTag_ta_CtnManifest` FOREIGN KEY (`cCtnManifestId`) REFERENCES `ta_CtnManifest` (`cCtnManifestId`)
      ,
   KEY `IX_ta_CtnTag_cCtnManifestId` (`cCtnManifestId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnBlob`
      (
         `cCtnBlobId`     char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnBlobDigest` varchar(128) COLLATE utf8mb4_0900_as_cs NOT NULL,
         `cCtnBlobSize`   bigint       NOT NULL,
         `ustamp`         datetime(3)     NOT NULL,
         `datestamp`      datetime(3)     NOT NULL,
         `json_object`    longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_CtnBlob` PRIMARY KEY  (`cCtnBlobId`),
         CONSTRAINT `UQ_ta_CtnBlob_cCtnBlobDigest` UNIQUE  (`cCtnBlobDigest`)
      
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnBlobLink`
      (
         `cCtnImageId` char(26) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnBlobId`  char(26) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `datestamp`   datetime(3) NOT NULL,
         CONSTRAINT `PK_ta_CtnBlobLink` PRIMARY KEY  (`cCtnImageId`, `cCtnBlobId`),
         CONSTRAINT `FK_ta_CtnBlobLink_ta_CtnImage` FOREIGN KEY (`cCtnImageId`) REFERENCES `ta_CtnImage` (`cCtnImageId`) ON DELETE CASCADE,
         CONSTRAINT `FK_ta_CtnBlobLink_ta_CtnBlob` FOREIGN KEY (`cCtnBlobId`) REFERENCES `ta_CtnBlob` (`cCtnBlobId`)
      ,
   KEY `IX_ta_CtnBlobLink_cCtnBlobId` (`cCtnBlobId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnManifestBlob`
      (
         `cCtnManifestId`        char(26)    COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnManifestBlobOrder` int         NOT NULL,
         `cCtnBlobId`            char(26)    COLLATE utf8mb4_0900_ai_ci NOT NULL,
         -- 'config' or 'layer'.
         `cCtnManifestBlobRole`  varchar(20) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         CONSTRAINT `PK_ta_CtnManifestBlob` PRIMARY KEY  (`cCtnManifestId`, `cCtnManifestBlobOrder`),
         CONSTRAINT `FK_ta_CtnManifestBlob_ta_CtnManifest` FOREIGN KEY (`cCtnManifestId`) REFERENCES `ta_CtnManifest` (`cCtnManifestId`) ON DELETE CASCADE,
         CONSTRAINT `FK_ta_CtnManifestBlob_ta_CtnBlob` FOREIGN KEY (`cCtnBlobId`) REFERENCES `ta_CtnBlob` (`cCtnBlobId`)
      ,
   KEY `IX_ta_CtnManifestBlob_cCtnBlobId` (`cCtnBlobId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnUpload`
      (
         `cCtnUploadId`   char(26) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnImageId`    char(26) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cRobotId`    char(26) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         -- Offset: how many bytes have been received. The temporary file on disk is named after the Id.
         `cCtnUploadSize` bigint   NOT NULL,
         `ustamp`         datetime(3) NOT NULL,
         `datestamp`      datetime(3) NOT NULL,
         `json_object`    longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_CtnUpload` PRIMARY KEY  (`cCtnUploadId`),
         CONSTRAINT `FK_ta_CtnUpload_ta_CtnImage` FOREIGN KEY (`cCtnImageId`) REFERENCES `ta_CtnImage` (`cCtnImageId`) ON DELETE CASCADE,
         CONSTRAINT `FK_ta_CtnUpload_ta_CtnRobot` FOREIGN KEY (`cRobotId`) REFERENCES `ta_Robot` (`cRobotId`)
      ,
   KEY `IX_ta_CtnUpload_cCtnImageId` (`cCtnImageId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnRootRobot`
      (
         `cRobotId`         char(26) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnRootId`          char(26) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         -- 'R' (pull) or 'W' (push; includes pull).
         `cCtnRootRobotAccess` char(1)  COLLATE utf8mb4_0900_as_cs NOT NULL,
         `ustamp`              datetime(3) NOT NULL,
         `datestamp`           datetime(3) NOT NULL,
         CONSTRAINT `PK_ta_CtnRobotRoot` PRIMARY KEY  (`cRobotId`, `cCtnRootId`),
         CONSTRAINT `CK_ta_CtnRobotRoot_Access` CHECK (`cCtnRootRobotAccess` IN ('R', 'W')),
         CONSTRAINT `FK_ta_CtnRobotRoot_ta_CtnRobot` FOREIGN KEY (`cRobotId`) REFERENCES `ta_Robot` (`cRobotId`) ON DELETE CASCADE,
         CONSTRAINT `FK_ta_CtnRobotRoot_ta_CtnRoot` FOREIGN KEY (`cCtnRootId`) REFERENCES `ta_CtnRoot` (`cCtnRootId`)
      ,
   KEY `IX_ta_CtnRobotRoot_cCtnRootId` (`cCtnRootId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnDeploy`
      (
         `cCtnDeployId`             char(26)       COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnImageId`              char(26)       COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnDeployState`          int            NOT NULL,
         -- 1 = SSH, 2 = Portainer.
         `cCtnDeployKind`           int            NOT NULL,
         -- 1 = Stack (compose), 2 = standalone container.
         `cCtnDeployMode`           int            NOT NULL,
         -- Comma-separated tag patterns (* and ?); empty = every tag.
         `cCtnDeployTagFilter`      varchar(256)   COLLATE utf8mb4_0900_as_cs NULL,
         -- Registry address the Docker server pulls from (host`:port`).
         `cCtnDeployRegistryHost`   varchar(255)   COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnDeployRegistryUser`   varchar(128)   COLLATE utf8mb4_0900_ai_ci NULL,
         `cCtnDeployRegistrySecret` longblob NULL,
         -- SSH host name, or the base URL of Portainer.
         `cCtnDeployHost`           varchar(255)   COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnDeployPort`           int            NULL,
         `cCtnDeployUser`           varchar(128)   COLLATE utf8mb4_0900_ai_ci NULL,
         -- 1 = SSH key, 2 = SSH password, 3 = Portainer access token.
         `cCtnDeployAuth`           int            NOT NULL,
         `cCtnDeploySecret`         longblob NULL,
         `cCtnDeployPassphrase`     longblob NULL,
         -- Pinned SSH host key or TLS certificate fingerprint.
         `cCtnDeployFingerprint`    varchar(128)   COLLATE utf8mb4_0900_as_cs NULL,
         `cCtnDeployEndpointId`     int            NULL,
         -- Compose folder (SSH) or stack name (Portainer).
         `cCtnDeployStack`          varchar(255)   COLLATE utf8mb4_0900_as_cs NULL,
         `cCtnDeployStackId`        int            NULL,
         `cCtnDeployService`        varchar(128)   COLLATE utf8mb4_0900_as_cs NULL,
         `cCtnDeployContainer`      varchar(128)   COLLATE utf8mb4_0900_as_cs NULL,
         `cCtnDeployImageVar`       varchar(128)   COLLATE utf8mb4_0900_as_cs NULL,
         -- Image line of a Stack deploy: 1 or NULL = ${variable}, 2 = name:tag@digest, 3 = name:tag.
         `cCtnDeployImageRef`       int            NULL,
         `ustamp`                   datetime(3)       NOT NULL,
         `datestamp`                datetime(3)       NOT NULL,
         `json_object`              longtext  COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `PK_ta_CtnDeploy` PRIMARY KEY  (`cCtnDeployId`),
         CONSTRAINT `UQ_ta_CtnDeploy_cCtnImageId` UNIQUE  (`cCtnImageId`),
         CONSTRAINT `FK_ta_CtnDeploy_ta_CtnImage` FOREIGN KEY (`cCtnImageId`) REFERENCES `ta_CtnImage` (`cCtnImageId`) ON DELETE CASCADE
      
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `ta_CtnDeployRun`
      (
         `cCtnDeployRunId`         char(26)      COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cCtnDeployId`            char(26)      COLLATE utf8mb4_0900_ai_ci NOT NULL,
         -- 1 = after push, 2 = manual, 3 = rollback.
         `cCtnDeployRunTrigger`    int           NOT NULL,
         `cCtnDeployRunTag`        varchar(128)  COLLATE utf8mb4_0900_as_cs NULL,
         `cCtnDeployRunDigest`     varchar(128)  COLLATE utf8mb4_0900_as_cs NOT NULL,
         `cCtnDeployRunPrevDigest` varchar(128)  COLLATE utf8mb4_0900_as_cs NULL,
         -- 0 = running, 1 = success, 2 = failed, 3 = skipped.
         `cCtnDeployRunResult`     int           NOT NULL,
         `cCtnDeployRunOutput`     longtext COLLATE utf8mb4_0900_ai_ci NULL,
         -- Compose/stack file content before the image line was rewritten to a variable.
         `cCtnDeployRunOldFile`    longtext COLLATE utf8mb4_0900_ai_ci NULL,
         `cCtnDeployRunBy_cUserId` char(26)      COLLATE utf8mb4_0900_ai_ci NULL,
         `cCtnDeployRunStarted`    datetime(3)      NOT NULL,
         `cCtnDeployRunFinished`   datetime(3)      NULL,
         `ustamp`                  datetime(3)      NOT NULL,
         `datestamp`               datetime(3)      NOT NULL,
         CONSTRAINT `PK_ta_CtnDeployRun` PRIMARY KEY  (`cCtnDeployRunId`),
         CONSTRAINT `FK_ta_CtnDeployRun_ta_CtnDeploy` FOREIGN KEY (`cCtnDeployId`) REFERENCES `ta_CtnDeploy` (`cCtnDeployId`) ON DELETE CASCADE,
         CONSTRAINT `FK_ta_CtnDeployRun_cCtnDeployRunBy_cUserId` FOREIGN KEY (`cCtnDeployRunBy_cUserId`) REFERENCES `ta_User` (`cUserId`) ON DELETE SET NULL
      ,
   KEY `IX_ta_CtnDeployRun_cCtnDeployId` (`cCtnDeployId`, `cCtnDeployRunStarted`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
