/* Business schema for a new MySQL 8.0+ database. Run once, before numbered modules.
   Requires sets/000-ulid.sql. Optional document-catalog foreign key is added when the catalog exists. */

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

CREATE TABLE `ta_Product` (
  `cProductId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUnitId` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductTypeId` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductIdIsUlid` bit  NOT NULL,
  `cProductName` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductSku` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_ProductTran` (
  `cProductTranId` char(34) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cDocName` varchar(25) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cBusinessPartnerId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductTranEmpRef_cEmpId` varchar(8) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cProductTranSelRef_cProductTranId` char(34) COLLATE utf8mb4_0900_ai_ci  NULL,
  `cProductTranRefNumber` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductTranStage` int  NOT NULL,
  `cProductTranRevision` int  NOT NULL,
  `cProductTranOrder` int  NOT NULL,
  `cProductTranDate` date  NOT NULL,
  `cProductTranNote` varchar(500) COLLATE utf8mb4_0900_ai_ci  NULL,
  `ustamp` datetime(3)  NOT NULL,
  `datestamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_ProductTranData` (
  `cProductTranDataId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductTranId` char(34) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cCurrencyId` varchar(25) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductTranDataOrder` int  NOT NULL,
  `cProductTranDataQty` decimal(18,2)  NOT NULL,
  `cProductTranDataAmountBase` decimal(19,4)  NOT NULL,
  `cProductTranDataAmountModif` decimal(19,4)  NOT NULL,
  `ustamp` datetime(3)  NOT NULL,
  `json_object` longtext COLLATE utf8mb4_0900_ai_ci  NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_ProductType` (
  `cProductTypeId` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductTypeDescription` varchar(500) COLLATE utf8mb4_0900_ai_ci  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_ShoeHelper` (
  `cShoeHelperId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cProductId` char(26) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cShoeModel` char(4) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cShoeType` int  NOT NULL,
  `cShoeVersion` int  NOT NULL,
  `cShoeSize` varchar(5) COLLATE utf8mb4_0900_ai_ci  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ta_Unit` (
  `cUnitId` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUnitSymbol` varchar(50) COLLATE utf8mb4_0900_ai_ci  NOT NULL,
  `cUnitDesc` varchar(255) COLLATE utf8mb4_0900_ai_ci  NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE `ta_BusinessPartner` ADD CONSTRAINT `PK_ta_BusinessPartner` PRIMARY KEY  (`cBusinessPartnerId`);

ALTER TABLE `ta_Currency` ADD CONSTRAINT `PK__ta_Curre__90C325330E812541` PRIMARY KEY  (`cCurrencyId`);

ALTER TABLE `ta_Emp` ADD CONSTRAINT `PK__ta_Emp__E1E4D37364305A01` PRIMARY KEY  (`cEmpId`);

ALTER TABLE `ta_Product` ADD CONSTRAINT `PK__ta_Produ__C2C11E67B50DC9A7` PRIMARY KEY  (`cProductId`);

ALTER TABLE `ta_ProductTran` ADD CONSTRAINT `PK__ta_Produ__70BAE9A9A0EDEA66` PRIMARY KEY  (`cProductTranId`);

ALTER TABLE `ta_ProductTranData` ADD CONSTRAINT `PK__ta_Produ__B7755D5A13E4EC34` PRIMARY KEY  (`cProductTranDataId`);

ALTER TABLE `ta_ProductType` ADD CONSTRAINT `PK__ta_Produ__D10973B1C446C2A7` PRIMARY KEY  (`cProductTypeId`);

CREATE UNIQUE  INDEX `NonClusteredIndex-20260716-093719`
ON `ta_ShoeHelper` (
  `cProductId` ASC
);

CREATE UNIQUE  INDEX `NonClusteredIndex-20260716-093729`
ON `ta_ShoeHelper` (
  `cShoeModel` ASC,
  `cShoeType` ASC,
  `cShoeVersion` ASC,
  `cShoeSize` ASC
);

ALTER TABLE `ta_ShoeHelper` ADD CONSTRAINT `PK__ta_ShoeH__C2C11E678A6A45F6` PRIMARY KEY  (`cShoeHelperId`);

ALTER TABLE `ta_Unit` ADD CONSTRAINT `PK__ta_Unit__0D6B678A413CC257` PRIMARY KEY  (`cUnitId`);

ALTER TABLE `ta_BusinessPartner` ADD CONSTRAINT `FK_ta_BusinessPartner_ta_Contact` FOREIGN KEY (`cContactId`) REFERENCES `ta_Contact` (`cContactId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_Emp` ADD CONSTRAINT `FK_ta_Emp_ta_Contact` FOREIGN KEY (`cContactId`) REFERENCES `ta_Contact` (`cContactId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_Product` ADD CONSTRAINT `FK_ta_Product_ta_Unit` FOREIGN KEY (`cUnitId`) REFERENCES `ta_Unit` (`cUnitId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_Product` ADD CONSTRAINT `FK_ta_Product_ta_ProductType` FOREIGN KEY (`cProductTypeId`) REFERENCES `ta_ProductType` (`cProductTypeId`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `ta_ProductTran` ADD CONSTRAINT `FK_ta_ProductTran_ta_ProductTran` FOREIGN KEY (`cProductTranSelRef_cProductTranId`) REFERENCES `ta_ProductTran` (`cProductTranId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_ProductTran` ADD CONSTRAINT `FK_ta_ProductTran_ta_Emp` FOREIGN KEY (`cProductTranEmpRef_cEmpId`) REFERENCES `ta_Emp` (`cEmpId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_ProductTran` ADD CONSTRAINT `FK_ta_ProductTran_ta_BusinessPartner` FOREIGN KEY (`cBusinessPartnerId`) REFERENCES `ta_BusinessPartner` (`cBusinessPartnerId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_ProductTranData` ADD CONSTRAINT `FK_ta_ProductTranData_ta_ProductTran` FOREIGN KEY (`cProductTranId`) REFERENCES `ta_ProductTran` (`cProductTranId`) ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE `ta_ProductTranData` ADD CONSTRAINT `FK_ta_ProductTranData_ta_Product` FOREIGN KEY (`cProductId`) REFERENCES `ta_Product` (`cProductId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE `ta_ProductTranData` ADD CONSTRAINT `FK_ta_ProductTranData_ta_Currency` FOREIGN KEY (`cCurrencyId`) REFERENCES `ta_Currency` (`cCurrencyId`) ON DELETE NO ACTION ON UPDATE NO ACTION;

DELIMITER $$
CREATE TRIGGER `tr_ta_Product_ulid` BEFORE INSERT ON `ta_Product` FOR EACH ROW
BEGIN
    IF NEW.`cProductId` IS NULL OR NEW.`cProductId` = '' THEN SET NEW.`cProductId` = NewUlidString(); END IF;
END$$
DELIMITER ;
/* Add the optional application document-catalog relationship only when present. */
DROP PROCEDURE IF EXISTS mysql_add_product_doc_fk;
DELIMITER $$
CREATE PROCEDURE mysql_add_product_doc_fk()
BEGIN
 IF EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ta_Doc') THEN
  ALTER TABLE ta_ProductTran ADD CONSTRAINT FK_ta_ProductTran_ta_Doc FOREIGN KEY(cDocName) REFERENCES ta_Doc(cDocName) ON DELETE CASCADE ON UPDATE CASCADE;
 END IF;
END$$
DELIMITER ;
CALL mysql_add_product_doc_fk();
DROP PROCEDURE mysql_add_product_doc_fk;

