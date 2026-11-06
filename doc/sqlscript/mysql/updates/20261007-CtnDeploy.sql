/* Deploy tables for existing registry installations; safe to rerun. */
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
