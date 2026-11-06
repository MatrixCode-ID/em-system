/* Core schema for a new MySQL 8.0+ database. Run once, before numbered modules.
   Requires sets/000-ulid.sql. Robot creation and table constraints are included. */

CREATE TABLE `ta_Address` (
  `cAddressId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cContactId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cAddressName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cAddressState` int  NOT NULL,
  `cAddressLocation` varchar(1000) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cAddressZip` varchar(10) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Comm` (
  `cCommId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cContactId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cCommType` int  NOT NULL,
  `cCommState` int  NOT NULL,
  `cCommValue` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cCommNote` varchar(500) COLLATE utf8mb4_0900_ai_ci  NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Contact` (
  `cContactId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cContactFullName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cContactState` int  NOT NULL,
  `cContactType` int  NOT NULL,
  `cContactNote` varchar(500) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cContactDefaultAddress_cAddressId` char(26) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cContactDefaultComm_cCommId` char(26) COLLATE utf8mb4_0900_ai_ci  NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Log` (
  `cLogId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserId` char(26) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cLogModule` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cLogAction` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cLogEntity` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cLogEntityId` char(26) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cLogSource` varchar(100) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cLogName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cLogRefNumber` varchar(50) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cLogStage` int DEFAULT (0) NOT NULL,
  `cLogOrder` int DEFAULT (-1) NOT NULL,
  `cLogRevision` int DEFAULT (1) NOT NULL,
  `cLogDate` date  NOT NULL,
  `cLogNote` varchar(500) COLLATE utf8mb4_0900_ai_ci  NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Meta` (
  `cMetaKey` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cMetaValue` longtext COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cMetaDescription` varchar(500) COLLATE utf8mb4_0900_ai_ci  NULL,
  `ustamp` datetime(3)  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Role` (
  `cRoleId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cRoleName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cRoleState` int  NOT NULL,
  `cRoleDescription` varchar(500) COLLATE utf8mb4_0900_ai_ci  NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_RoleClaim` (
  `cRoleId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cClaimName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_SystemSession` (
  `cSystemSessionId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cSystemSessionAccountId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cSystemSessionHash` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cSystemSessionState` int  NOT NULL,
  `cSystemSessionExpiry` datetime(3)  NOT NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_User` (
  `cUserId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserAccount` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cContactId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserState` int  NOT NULL,
  `cUserIsAdmin` bit  NOT NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_UserClaim` (
  `cUserClaimId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserClaimName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserClaimStart` datetime(3)  NOT NULL,
  `cUserClaimExpiry` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_UserCredential` (
  `cCredentialId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cCredentialType` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cCredentialState` int  NOT NULL,
  `cCredentialKey` varchar(255) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cCredentialSecret` varchar(255) COLLATE utf8mb4_0900_ai_ci  NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_UserRole` (
  `cUserId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cRoleId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserRoleStart` datetime(3)  NULL,
  `cUserRoleExpiry` datetime(3)  NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_UserSession` (
  `cUserSessionId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserSessionHash` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUserSessionState` int  NOT NULL,
  `cUserSessionExpiry` datetime(3)  NOT NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE `ta_Address` ADD CONSTRAINT `PK_ta_Address_1` PRIMARY KEY  (`cAddressId`);

ALTER TABLE `ta_Comm` ADD CONSTRAINT `PK_ta_Comm_1` PRIMARY KEY  (`cCommId`);

ALTER TABLE `ta_Contact` ADD CONSTRAINT `PK_ta_Contact` PRIMARY KEY  (`cContactId`);

CREATE  INDEX `IX_ta_Log_Entity`
ON `ta_Log` (
  `cLogEntity` ASC,
  `cLogEntityId` ASC,
  `datestamp` DESC
);

CREATE  INDEX `IX_ta_Log_User`
ON `ta_Log` (
  `cUserId` ASC,
  `datestamp` DESC
);

CREATE  INDEX `IX_ta_Log_ModuleAction`
ON `ta_Log` (
  `cLogModule` ASC,
  `cLogAction` ASC,
  `datestamp` DESC
);

ALTER TABLE `ta_Log` ADD CONSTRAINT `PK_ta_Log` PRIMARY KEY  (`cLogId`);

ALTER TABLE `ta_Meta` ADD CONSTRAINT `PK_ta_Meta` PRIMARY KEY  (`cMetaKey`);

ALTER TABLE `ta_Role` ADD CONSTRAINT `UQ_ta_Role_cRoleName` UNIQUE  (`cRoleName` ASC);

ALTER TABLE `ta_Role` ADD CONSTRAINT `PK_ta_Role` PRIMARY KEY  (`cRoleId`);

ALTER TABLE `ta_RoleClaim` ADD CONSTRAINT `PK_ta_RoleClaim` PRIMARY KEY  (`cRoleId`, `cClaimName`);

CREATE UNIQUE  INDEX `UQ_ta_SystemSession_Hash`
ON `ta_SystemSession` (
  `cSystemSessionHash` ASC
);

CREATE  INDEX `IX_ta_SystemSession_cSystemSessionAccountId`
ON `ta_SystemSession` (
  `cSystemSessionAccountId` ASC
);

ALTER TABLE `ta_SystemSession` ADD CONSTRAINT `PK_ta_SystemSession` PRIMARY KEY  (`cSystemSessionId`);

ALTER TABLE `ta_User` ADD CONSTRAINT `UQ_ta_User_cUserAccount` UNIQUE  (`cUserAccount` ASC);

ALTER TABLE `ta_User` ADD CONSTRAINT `PK_ta_User` PRIMARY KEY  (`cUserId`);

ALTER TABLE `ta_UserClaim` ADD CONSTRAINT `UQ_ta_UserClaim_cUserId_cUserClaimName` UNIQUE  (`cUserId` ASC, `cUserClaimName` ASC);

ALTER TABLE `ta_UserClaim` ADD CONSTRAINT `PK_ta_UserClaim_1` PRIMARY KEY  (`cUserClaimId`);

ALTER TABLE `ta_UserCredential` ADD CONSTRAINT `UQ_ta_UserCredential_cUserId_cCredentialType` UNIQUE  (`cUserId` ASC, `cCredentialType` ASC);

ALTER TABLE `ta_UserCredential` ADD CONSTRAINT `PK_ta_UserCredential` PRIMARY KEY  (`cCredentialId`);

ALTER TABLE `ta_UserRole` ADD CONSTRAINT `PK_ta_UserRole` PRIMARY KEY  (`cUserId`, `cRoleId`);

CREATE UNIQUE  INDEX `UQ_ta_UserSession_Hash`
ON `ta_UserSession` (
  `cUserSessionHash` ASC
);

CREATE  INDEX `IX_ta_UserSession_cUserId`
ON `ta_UserSession` (
  `cUserId` ASC
);

ALTER TABLE `ta_UserSession` ADD CONSTRAINT `PK_ta_UserSession` PRIMARY KEY  (`cUserSessionId`);

CREATE TABLE `ta_Robot`
      (
         `cRobotId`            char(26)     COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cRobotName`          varchar(255) COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cRobotOwner_cUserId` char(26)     COLLATE utf8mb4_0900_ai_ci NULL,
         `cRobotState`         int          NOT NULL,
         `cRobotDescription`   varchar(500) COLLATE utf8mb4_0900_ai_ci NULL,
         -- SHA-256 hex of the token; the token itself is never stored.
         `cRobotTokenHash`     varchar(64)  COLLATE utf8mb4_0900_as_cs NOT NULL,
         `cRobotTokenPrefix`   varchar(20)  COLLATE utf8mb4_0900_ai_ci NOT NULL,
         `cRobotTokenExpiry`   datetime(3)     NULL,
         `cRobotTokenLastUsed` datetime(3)     NULL,
         `ustamp`                 datetime(3)     NOT NULL,
         `datestamp`              datetime(3)     NOT NULL,
         `json_object`            longtext COLLATE utf8mb4_0900_ai_ci NULL,
         CONSTRAINT `FK_ta_Robot_Owner` FOREIGN KEY (`cRobotOwner_cUserId`) REFERENCES `ta_User` (`cUserId`) ON DELETE SET NULL,
         CONSTRAINT `PK_ta_CtnRobot` PRIMARY KEY  (`cRobotId`),
         CONSTRAINT `UQ_ta_CtnRobot_cRobotName` UNIQUE  (`cRobotName`),
         CONSTRAINT `UQ_ta_CtnRobot_cRobotTokenHash` UNIQUE  (`cRobotTokenHash`)
      ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE `ta_Address` ADD CONSTRAINT `FK_ta_Address_ta_Contact` FOREIGN KEY (`cContactId`) REFERENCES `ta_Contact` (`cContactId`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `ta_Comm` ADD CONSTRAINT `FK_ta_Comm_ta_Contact` FOREIGN KEY (`cContactId`) REFERENCES `ta_Contact` (`cContactId`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `ta_Contact` ADD CONSTRAINT `FK_ta_Contact_ta_Address` FOREIGN KEY (`cContactDefaultAddress_cAddressId`) REFERENCES `ta_Address` (`cAddressId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_Contact` ADD CONSTRAINT `FK_ta_Contact_ta_Comm` FOREIGN KEY (`cContactDefaultComm_cCommId`) REFERENCES `ta_Comm` (`cCommId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_Log` ADD CONSTRAINT `FK_ta_Log_ta_User` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_RoleClaim` ADD CONSTRAINT `FK_ta_RoleClaim_ta_Role` FOREIGN KEY (`cRoleId`) REFERENCES `ta_Role` (`cRoleId`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `ta_User` ADD CONSTRAINT `FK_ta_User_ta_Contact` FOREIGN KEY (`cContactId`) REFERENCES `ta_Contact` (`cContactId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_UserClaim` ADD CONSTRAINT `FK_ta_UserClaim_ta_User` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `ta_UserCredential` ADD CONSTRAINT `FK_ta_UserCredential_ta_User` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `ta_UserRole` ADD CONSTRAINT `FK_ta_UserRole_ta_Role` FOREIGN KEY (`cRoleId`) REFERENCES `ta_Role` (`cRoleId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_UserRole` ADD CONSTRAINT `FK_ta_UserRole_ta_User` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_UserSession` ADD CONSTRAINT `FK_ta_UserSession_ta_User` FOREIGN KEY (`cUserId`) REFERENCES `ta_User` (`cUserId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

DELIMITER $$
CREATE TRIGGER `tr_ta_Log_ulid` BEFORE INSERT ON `ta_Log` FOR EACH ROW
BEGIN
    IF NEW.`cLogId` IS NULL OR NEW.`cLogId` = '' THEN SET NEW.`cLogId` = NewUlidString(); END IF;
END$$
DELIMITER ;
