/* Business schema for a new MySQL 8.0+ database: business partners, currencies, units and employees. Run once,
   before numbered modules. Requires sets/000-ulid.sql. Products and product types live in tables/120-product.sql.
   An installation from before 2026-11-12 runs updates/20261112-Unit-Stamp.sql and
   updates/20261112-DropLegacyProduct.sql instead of this script. */

CREATE TABLE `ta_BusinessPartner` (
  `cBusinessPartnerId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cContactId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Currency` (
  `cCurrencyId` varchar(25) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cCurrencyName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Emp` (
  `cEmpId` varchar(8) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cContactId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cEmpPositionCurent` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Unit` (
  `cUnitId` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUnitSymbol` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUnitDesc` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `ustamp` datetime(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  `datestamp` datetime(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE `ta_BusinessPartner` ADD CONSTRAINT `PK_ta_BusinessPartner` PRIMARY KEY  (`cBusinessPartnerId`);

ALTER TABLE `ta_Currency` ADD CONSTRAINT `PK__ta_Curre__90C325330E812541` PRIMARY KEY  (`cCurrencyId`);

ALTER TABLE `ta_Emp` ADD CONSTRAINT `PK__ta_Emp__E1E4D37364305A01` PRIMARY KEY  (`cEmpId`);

ALTER TABLE `ta_Unit` ADD CONSTRAINT `PK__ta_Unit__0D6B678A413CC257` PRIMARY KEY  (`cUnitId`);

ALTER TABLE `ta_BusinessPartner` ADD CONSTRAINT `FK_ta_BusinessPartner_ta_Contact` FOREIGN KEY (`cContactId`) REFERENCES `ta_Contact` (`cContactId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_Emp` ADD CONSTRAINT `FK_ta_Emp_ta_Contact` FOREIGN KEY (`cContactId`) REFERENCES `ta_Contact` (`cContactId`) ON DELETE NO ACTION ON UPDATE NO ACTION;
