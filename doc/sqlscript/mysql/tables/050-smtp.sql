/* MySQL 8.0+: SMTP profiles. Run on the core database; safe to rerun.
   The generated column enforces the SQL Server filtered unique index: any number
   of non-default profiles, but no more than one default profile. No data is seeded. */
CREATE TABLE IF NOT EXISTS `ta_Smtp` (
    `cSmtpId` char(26) NOT NULL,
    `cSmtpName` varchar(255) NOT NULL,
    `cSmtpState` int NOT NULL DEFAULT 0,
    `cSmtpRevision` int NOT NULL DEFAULT 1,
    `cSmtpNote` varchar(500) NULL,
    `cSmtpDefault` bit NOT NULL DEFAULT b'0',
    `cSmtpHost` varchar(255) NOT NULL,
    `cSmtpPort` int NOT NULL DEFAULT 587,
    `cSmtpSecurity` int NOT NULL DEFAULT 1,
    `cSmtpAuthenticate` bit NOT NULL DEFAULT b'1',
    `cSmtpUsername` varchar(255) NOT NULL,
    `cSmtpPassword` longblob NULL,
    `cSmtpFromAddress` varchar(320) NOT NULL,
    `cSmtpFromName` varchar(255) NOT NULL,
    `cSmtpTimeoutSeconds` int NOT NULL DEFAULT 30,
    `ustamp` datetime(3) NOT NULL,
    `datestamp` datetime(3) NOT NULL,
    `json_object` longtext NULL,
    `mysql_smtp_default_key` tinyint GENERATED ALWAYS AS (CASE WHEN `cSmtpDefault` = b'1' THEN 1 ELSE NULL END) STORED,
    PRIMARY KEY (`cSmtpId`),
    UNIQUE KEY `UQ_ta_Smtp_Name` (`cSmtpName`),
    UNIQUE KEY `UX_ta_Smtp_Default` (`mysql_smtp_default_key`),
    CONSTRAINT `CK_ta_Smtp_Name` CHECK (CHAR_LENGTH(TRIM(`cSmtpName`)) > 0),
    CONSTRAINT `CK_ta_Smtp_State` CHECK (`cSmtpState` IN (0, 1)),
    CONSTRAINT `CK_ta_Smtp_Revision` CHECK (`cSmtpRevision` >= 1),
    CONSTRAINT `CK_ta_Smtp_Port` CHECK (`cSmtpPort` BETWEEN 1 AND 65535),
    CONSTRAINT `CK_ta_Smtp_Security` CHECK (`cSmtpSecurity` IN (0, 1, 2)),
    CONSTRAINT `CK_ta_Smtp_Timeout` CHECK (`cSmtpTimeoutSeconds` BETWEEN 5 AND 120),
    CONSTRAINT `CK_ta_Smtp_Json` CHECK (`json_object` IS NULL OR JSON_VALID(`json_object`))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
